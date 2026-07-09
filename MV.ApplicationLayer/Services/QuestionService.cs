using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.RequestModel.Question;
using MV.DomainLayer.DTO.ResponseModel.Question;
using MV.DomainLayer.Entities;
using Pgvector;

namespace MV.ApplicationLayer.Services;

public class QuestionService : IQuestionService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITutorAiClient _aiClient;
    private readonly ILogger<QuestionService> _logger;

    public QuestionService(
        IUnitOfWork unitOfWork,
        ITutorAiClient aiClient,
        ILogger<QuestionService> logger)
    {
        _unitOfWork = unitOfWork;
        _aiClient = aiClient;
        _logger = logger;
    }

    public async Task<QuestionResponse> CreateAsync(
        CreateQuestionRequest request, string? createdBy, CancellationToken ct = default)
    {
        var entity = new QuestionBank
        {
            Id = Guid.NewGuid(),
            SubjectId = request.SubjectId,
            GradeLevelId = request.GradeLevelId,
            ChapterId = request.ChapterId,
            QuestionTypeId = request.QuestionTypeId,
            Difficulty = request.Difficulty,
            Content = request.Content,
            Solution = request.Solution,
            SolutionSource = request.SolutionSource,
            ReviewStatus = string.IsNullOrWhiteSpace(request.ReviewStatus) ? "pending_review" : request.ReviewStatus,
            CreatedBy = createdBy,
        };

        await _unitOfWork.QuestionRepository.AddAsync(entity);
        await _unitOfWork.SaveChangesAsync();   // trigger DB tự tính content_hash

        // Embed content -> vector (best-effort: lỗi thì câu vẫn được lưu, embed lại sau).
        await TryEmbedAsync(entity, ct);

        return ToResponse(entity);
    }

    public async Task<QuestionResponse?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _unitOfWork.QuestionRepository.GetByIdAsync(id);
        return entity == null ? null : ToResponse(entity);
    }

    public async Task<PagedList<QuestionResponse>> GetPagedAsync(
        int pageNumber, int pageSize,
        int? subjectId, int? gradeLevelId, int? chapterId,
        string? reviewStatus, string? search,
        CancellationToken ct = default)
    {
        var paged = await _unitOfWork.QuestionRepository.GetPagedAsync(
            pageNumber, pageSize, subjectId, gradeLevelId, chapterId, reviewStatus, search);

        var items = paged.Select(ToResponse).ToList();
        return new PagedList<QuestionResponse>(items, paged.TotalCount, paged.CurrentPage, paged.PageSize);
    }

    public async Task<QuestionResponse?> UpdateAsync(
        Guid id, UpdateQuestionRequest request, CancellationToken ct = default)
    {
        var entity = await _unitOfWork.QuestionRepository.GetByIdAsync(id);
        if (entity == null) return null;

        bool contentChanged = entity.Content != request.Content;

        entity.SubjectId = request.SubjectId;
        entity.GradeLevelId = request.GradeLevelId;
        entity.ChapterId = request.ChapterId;
        entity.QuestionTypeId = request.QuestionTypeId;
        entity.Difficulty = request.Difficulty;
        entity.Content = request.Content;
        entity.Solution = request.Solution;
        entity.SolutionSource = request.SolutionSource;
        if (!string.IsNullOrWhiteSpace(request.ReviewStatus))
            entity.ReviewStatus = request.ReviewStatus;

        _unitOfWork.QuestionRepository.Update(entity);
        await _unitOfWork.SaveChangesAsync();   // trigger cập nhật content_hash nếu content đổi

        // Chỉ re-embed khi content đổi (vector cũ không còn khớp đề mới).
        if (contentChanged)
            await TryEmbedAsync(entity, ct);

        return ToResponse(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _unitOfWork.QuestionRepository.GetByIdAsync(id);
        if (entity == null) return false;

        _unitOfWork.QuestionRepository.Remove(entity);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    // ── Embed helper ──────────────────────────────────────────────
    // Gọi tutora-ai embed content -> vector. Set embedded_hash = content_hash
    // (đọc lại sau save vì trigger DB tính) để đánh dấu "đã embed đúng content này".
    private async Task TryEmbedAsync(QuestionBank entity, CancellationToken ct)
    {
        try
        {
            var vector = await _aiClient.EmbedAsync(entity.Id.ToString(), entity.Content, ct);
            if (vector == null) return;   // embed lỗi -> để job/backfill sau lo

            entity.Embedding = new Vector(vector);
            entity.EmbeddedHash = entity.ContentHash;   // content_hash do trigger tính, đã có trong entity sau save
            _unitOfWork.QuestionRepository.Update(entity);
            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embed câu hỏi {Id} thất bại — sẽ embed lại sau.", entity.Id);
        }
    }

    // static để dùng chung; nav (ChapterNav/QuestionType/Subject/Gradelevel) cần
    // được Include khi query để có Name (null-safe nếu chưa Include).
    internal static QuestionResponse ToResponse(QuestionBank e) => new()
    {
        Id = e.Id,
        SubjectId = e.SubjectId,
        SubjectName = e.Subject?.Subjectname,
        GradeLevelId = e.GradeLevelId,
        GradeName = e.Gradelevel?.Gradename,
        ChapterId = e.ChapterId,
        ChapterName = e.ChapterNav?.Name,
        QuestionTypeId = e.QuestionTypeId,
        QuestionTypeName = e.QuestionType?.Name,
        Difficulty = e.Difficulty,
        Content = e.Content,
        Solution = e.Solution,
        SolutionSource = e.SolutionSource,
        ImageUrls = e.ImageUrls ?? new(),
        SourceDocumentId = e.SourceDocumentId,
        SourcePage = e.SourcePage,
        ReviewStatus = e.ReviewStatus,
        ReviewedBy = e.ReviewedBy,
        ReviewedAt = e.ReviewedAt,
        HasEmbedding = e.Embedding != null && e.ContentHash != null && e.ContentHash == e.EmbeddedHash,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
    };
}
