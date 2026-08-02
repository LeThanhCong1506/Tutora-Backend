using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO.ResponseModel.KnowledgeBase;
using MV.DomainLayer.Exceptions;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Knowledge Base Tutora.
public class KnowledgeBaseService : IKnowledgeBaseService
{
    // Cùng giới hạn với endpoint tutora-ai (/api/v1/kb/upload chặn >20MB).
    private const long MaxBytes = 20L * 1024 * 1024;

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".docx", ".xlsx", ".md", ".markdown" };

    private readonly ITutorAiClient _aiClient;
    private readonly IAppDbContext _context;

    public KnowledgeBaseService(ITutorAiClient aiClient, IAppDbContext context)
    {
        _aiClient = aiClient;
        _context = context;
    }

    public async Task<KbUploadResponse> UploadAsync(IFormFile file, string? uploadedBy, CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("Vui lòng chọn file.");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            throw new ArgumentException("Chỉ nhận file PDF, DOCX, XLSX hoặc Markdown (.md).");

        if (file.Length > MaxBytes)
            throw new ArgumentException("File quá lớn (giới hạn 20MB).");

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }

        // Chỉ upload mới cần AI (extract/chunk/embed). tutora-ai tự insert vào DB chung.
        var result = await _aiClient.KbUploadAsync(bytes, file.FileName, uploadedBy, ct);
        if (result == null)
            throw new ExternalApiException("Nạp tài liệu vào Knowledge Base thất bại (dịch vụ AI không phản hồi hoặc file không đọc được).");

        return new KbUploadResponse
        {
            DocumentId = result.DocumentId,
            FileName = result.FileName,
            ChunkCount = result.ChunkCount,
        };
    }

    public async Task<List<KbDocumentResponse>> ListDocumentsAsync(CancellationToken ct = default)
    {
        return await _context.TutoraKbDocuments
            .AsNoTracking()
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new KbDocumentResponse
            {
                Id = d.Id.ToString(),
                FileName = d.FileName,
                SourceType = d.SourceType,
                ChunkCount = d.ChunkCount,
                Status = d.Status,
                CreatedAt = d.CreatedAt,
            })
            .ToListAsync(ct);
    }

    public async Task<KbDocumentDetailResponse?> GetDocumentDetailAsync(string documentId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(documentId, out var id))
            throw new ArgumentException("Mã tài liệu không hợp lệ.");

        var doc = await _context.TutoraKbDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc == null) return null;

        // Ghép nội dung các chunk theo thứ tự để hiển thị nguyên văn trong modal.
        var parts = await _context.TutoraKbChunks
            .AsNoTracking()
            .Where(c => c.DocumentId == id)
            .OrderBy(c => c.ChunkIndex)
            .Select(c => c.Content)
            .ToListAsync(ct);

        return new KbDocumentDetailResponse
        {
            Id = doc.Id.ToString(),
            FileName = doc.FileName,
            SourceType = doc.SourceType,
            ChunkCount = doc.ChunkCount,
            Status = doc.Status,
            CreatedAt = doc.CreatedAt,
            Content = string.Join("\n\n", parts),
        };
    }

    public async Task<KbDocumentDetailResponse> UpdateContentAsync(string documentId, string content, CancellationToken ct = default)
    {
        if (!Guid.TryParse(documentId, out _))
            throw new ArgumentException("Mã tài liệu không hợp lệ.");
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Nội dung không được để trống.");

        // Sửa nội dung PHẢI qua tutora-ai: chunk lại + re-embed text mới (Gemini), thay chunk cũ.
        var chunkCount = await _aiClient.KbUpdateContentAsync(documentId, content, ct);
        if (chunkCount == null)
            throw new ExternalApiException("Cập nhật nội dung thất bại (dịch vụ AI không phản hồi hoặc nội dung không hợp lệ).");

        // Đọc lại chi tiết mới từ DB (tutora-ai vừa ghi vào bảng chung).
        var detail = await GetDocumentDetailAsync(documentId, ct);
        return detail ?? throw new ExternalApiException("Không đọc lại được tài liệu sau khi cập nhật.");
    }

    public async Task DeleteDocumentAsync(string documentId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(documentId, out var id))
            throw new ArgumentException("Mã tài liệu không hợp lệ.");

        var doc = await _context.TutoraKbDocuments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc == null) return;

        _context.TutoraKbDocuments.Remove(doc);
        await _context.SaveChangesAsync(ct);
    }
}
