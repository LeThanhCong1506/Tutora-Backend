using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel.Question;
using MV.DomainLayer.Entities;
using Pgvector;

namespace MV.ApplicationLayer.Services;

public class SourceDocumentService : ISourceDocumentService
{
    // giới hạn số trang để giữ chất lượng extract
    private const int MaxPages = 20;
    private const string PdfBucket = "question-bank-pdf";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IFileStorageService _storage;
    private readonly ITutorAiClient _aiClient;
    private readonly INotificationService _notificationService;
    private readonly ILogger<SourceDocumentService> _logger;

    public SourceDocumentService(
        IUnitOfWork unitOfWork,
        IFileStorageService storage,
        ITutorAiClient aiClient,
        INotificationService notificationService,
        ILogger<SourceDocumentService> logger)
    {
        _unitOfWork = unitOfWork;
        _storage = storage;
        _aiClient = aiClient;
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>
    /// Báo cho staff/admin đã upload biết PDF trích xuất xong (in-app + push qua hub).
    /// Best-effort: lỗi thông báo không được làm hỏng luồng trích xuất.
    /// </summary>
    private async Task NotifyExtractionDoneAsync(SourceDocument doc, int questionCount)
    {
        if (string.IsNullOrWhiteSpace(doc.UploadedBy)) return;
        try
        {
            await _notificationService.CreateNotificationAsync(new NotificationRequest
            {
                Userid = doc.UploadedBy!,
                Title = "Trích xuất PDF hoàn tất",
                Message = questionCount > 0
                    ? $"Đã trích {questionCount} câu hỏi từ \"{doc.FileName}\" thành công."
                    : $"Không trích được câu hỏi nào từ \"{doc.FileName}\".",
                Type = NotificationType.Message,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gửi thông báo trích xuất PDF {Doc} thất bại.", doc.Id);
        }
    }

    public async Task<UploadPdfResponse> UploadAndExtractAsync(
        IFormFile file,
        int? defaultSubjectId,
        int? defaultGradeLevelId,
        string? uploadedBy,
        CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("File PDF rỗng.");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".pdf")
            throw new ArgumentException("Chỉ nhận file PDF.");

        // subject/grade BẮT BUỘC — câu hỏi lưu ra cần FK hợp lệ. Không default 0
        // (grade_level_id=0 không tồn tại trong grade_levels -> vỡ FK).
        if (defaultSubjectId is null or <= 0)
            throw new ArgumentException("Vui lòng chọn môn học cho tài liệu.");
        if (defaultGradeLevelId is null or <= 0)
            throw new ArgumentException("Vui lòng chọn khối lớp cho tài liệu.");

        // Đọc bytes 1 lần (dùng cho cả đếm trang + gửi AI extract).
        byte[] pdfBytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, ct);
            pdfBytes = ms.ToArray();
        }

        // Validate ≤20 trang TRƯỚC khi làm gì khác.
        int pageCount = PDFtoImage.Conversion.GetPageCount(pdfBytes);
        if (pageCount > MaxPages)
            throw new ArgumentException($"Tài liệu {pageCount} trang, vượt giới hạn {MaxPages} trang.");

        // Upload file gốc (để staff đối chiếu). Reset stream vì đã đọc ở trên.
        var fileUrl = await _storage.UploadFileAsync(PdfBucket, uploadedBy ?? "", file);

        var doc = new SourceDocument
        {
            Id = Guid.NewGuid(),
            FileUrl = fileUrl,
            FileName = file.FileName,
            PageCount = pageCount,
            DefaultSubjectId = defaultSubjectId,
            DefaultGradeLevelId = defaultGradeLevelId,
            Status = "processing",
            UploadedBy = uploadedBy,
        };
        await _unitOfWork.SourceDocumentRepository.AddAsync(doc);
        await _unitOfWork.SaveChangesAsync();

        // Gọi tutora-ai extract PDF -> list câu.
        var extracted = await _aiClient.ExtractPdfAsync(pdfBytes, file.FileName, ct);
        if (extracted == null)
        {
            doc.Status = "failed";
            doc.ErrorMessage = "AI extract thất bại (tutora-ai không phản hồi hoặc lỗi).";
            _unitOfWork.SourceDocumentRepository.Update(doc);
            await _unitOfWork.SaveChangesAsync();
            return Fail(doc, "AI extract thất bại. Vui lòng thử lại.");
        }

        // Lưu câu ở trạng thái pending_review. subject/grade lấy default của document.
        // Ảnh crop (base64) -> upload Cloudinary -> gán image_urls (best-effort).
        var questions = new List<QuestionBank>();
        foreach (var q in extracted.Where(x => !string.IsNullOrWhiteSpace(x.Content)))
        {
            var imageUrls = await UploadFigureImagesAsync(q.Images, uploadedBy, ct);
            questions.Add(new QuestionBank
            {
                Id = Guid.NewGuid(),
                SubjectId = defaultSubjectId.Value,        // đã validate > 0 ở trên
                GradeLevelId = defaultGradeLevelId.Value,
                Chapter = q.Chapter,
                ProblemType = q.ProblemType,
                Content = q.Content,
                Solution = q.Solution,
                ImageUrls = imageUrls,
                SourceDocumentId = doc.Id,
                SourcePage = q.Page,
                ReviewStatus = "pending_review",
                CreatedBy = uploadedBy,
            });
        }

        if (questions.Count == 0)
        {
            doc.Status = "done";
            doc.QuestionsExtracted = 0;
            _unitOfWork.SourceDocumentRepository.Update(doc);
            await _unitOfWork.SaveChangesAsync();
            await NotifyExtractionDoneAsync(doc, 0);
            return Fail(doc, "Không tách được câu hỏi nào từ PDF.");
        }

        await _unitOfWork.SourceDocumentRepository.AddQuestionsAsync(questions);
        doc.QuestionsExtracted = questions.Count;
        doc.Status = "done";
        _unitOfWork.SourceDocumentRepository.Update(doc);
        await _unitOfWork.SaveChangesAsync();   // trigger tính content_hash cho từng câu

        // Embed từng câu (best-effort). Câu nào embed lỗi -> vector null, embed lại sau.
        int embedded = 0;
        foreach (var q in questions)
        {
            try
            {
                var vector = await _aiClient.EmbedAsync(q.Id.ToString(), q.Content, ct);
                if (vector == null) continue;
                q.Embedding = new Vector(vector);
                q.EmbeddedHash = q.ContentHash;   // content_hash đã có sau save (trigger)
                _unitOfWork.QuestionRepository.Update(q);
                embedded++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Embed câu {Id} (từ PDF {Doc}) thất bại.", q.Id, doc.Id);
            }
        }
        if (embedded > 0)
            await _unitOfWork.SaveChangesAsync();

        await NotifyExtractionDoneAsync(doc, questions.Count);

        return new UploadPdfResponse
        {
            SourceDocumentId = doc.Id,
            FileName = doc.FileName,
            PageCount = doc.PageCount,
            QuestionsExtracted = questions.Count,
            QuestionsEmbedded = embedded,
            Status = doc.Status,
            Message = $"Đã tách {questions.Count} câu (embed {embedded}). Vui lòng duyệt trước khi publish.",
            Questions = questions.Select(ToResponse).ToList(),
        };
    }

    public async Task<PagedList<SourceDocumentResponse>> GetHistoryAsync(
        int pageNumber, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _unitOfWork.SourceDocumentRepository.GetHistoryAsync(pageNumber, pageSize);
        var mapped = items.Select(DocToResponse).ToList();
        return new PagedList<SourceDocumentResponse>(mapped, total, pageNumber, pageSize);
    }

    public async Task<SourceDocumentDetailResponse?> GetDetailAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await _unitOfWork.SourceDocumentRepository.GetWithQuestionsAsync(id);
        if (doc == null) return null;
        var detail = new SourceDocumentDetailResponse
        {
            Questions = doc.Questions.OrderBy(q => q.SourcePage).Select(ToResponse).ToList(),
        };
        FillDoc(detail, doc);
        return detail;
    }

    private static SourceDocumentResponse DocToResponse(SourceDocument d)
    {
        var r = new SourceDocumentResponse();
        FillDoc(r, d);
        return r;
    }

    private static void FillDoc(SourceDocumentResponse r, SourceDocument d)
    {
        r.Id = d.Id;
        r.FileName = d.FileName;
        r.FileUrl = d.FileUrl;
        r.PageCount = d.PageCount;
        r.DefaultSubjectId = d.DefaultSubjectId;
        r.DefaultGradeLevelId = d.DefaultGradeLevelId;
        r.Status = d.Status;
        r.QuestionsExtracted = d.QuestionsExtracted;
        r.ErrorMessage = d.ErrorMessage;
        r.UploadedBy = d.UploadedBy;
        r.CreatedAt = d.CreatedAt;
    }

    private const string ImageBucket = "question-bank-images";

    // Upload từng ảnh base64 (PNG crop từ PDF) -> Cloudinary. Lỗi 1 ảnh không chặn
    // cả câu (bỏ qua ảnh đó). Trả list URL.
    private async Task<List<string>> UploadFigureImagesAsync(
        List<string> base64Images, string? uploadedBy, CancellationToken ct)
    {
        var urls = new List<string>();
        foreach (var b64 in base64Images ?? new())
        {
            if (string.IsNullOrWhiteSpace(b64)) continue;
            try
            {
                var bytes = Convert.FromBase64String(b64);
                var url = await _storage.UploadImageBytesAsync(
                    ImageBucket, uploadedBy ?? "", bytes, $"{Guid.NewGuid()}.png");
                urls.Add(url);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Upload ảnh crop thất bại — bỏ qua ảnh này.");
            }
        }
        return urls;
    }

    private static QuestionResponse ToResponse(QuestionBank e) => QuestionService.ToResponse(e);

    private static UploadPdfResponse Fail(SourceDocument doc, string message) => new()
    {
        SourceDocumentId = doc.Id,
        FileName = doc.FileName,
        PageCount = doc.PageCount,
        QuestionsExtracted = doc.QuestionsExtracted,
        QuestionsEmbedded = 0,
        Status = doc.Status,
        Message = message,
    };
}
