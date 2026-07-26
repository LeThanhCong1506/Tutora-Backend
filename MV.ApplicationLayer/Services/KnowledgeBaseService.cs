using Microsoft.AspNetCore.Http;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO.ResponseModel.KnowledgeBase;
using MV.DomainLayer.Exceptions;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Forward KB Tutora (upload/list/delete) sang tutora-ai. KB lưu hoàn toàn bên tutora-ai
/// (bảng tutora_kb_*); service này chỉ validate đầu vào + dịch lỗi client thành exception
/// cho controller — KHÔNG chạm DbContext/repository của .NET.
/// </summary>
public class KnowledgeBaseService : IKnowledgeBaseService
{
    // Cùng giới hạn với endpoint tutora-ai (/api/v1/kb/upload chặn >20MB).
    private const long MaxBytes = 20L * 1024 * 1024;

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".docx", ".xlsx" };

    private readonly ITutorAiClient _aiClient;

    public KnowledgeBaseService(ITutorAiClient aiClient)
    {
        _aiClient = aiClient;
    }

    public async Task<KbUploadResponse> UploadAsync(IFormFile file, string? uploadedBy, CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("Vui lòng chọn file.");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            throw new ArgumentException("Chỉ nhận file PDF, DOCX hoặc XLSX.");

        if (file.Length > MaxBytes)
            throw new ArgumentException("File quá lớn (giới hạn 20MB).");

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }

        var result = await _aiClient.KbUploadAsync(bytes, file.FileName, uploadedBy, ct);
        if (result == null)
            throw new ExternalApiException("Nạp tài liệu vào thất bại.");

        return new KbUploadResponse
        {
            DocumentId = result.DocumentId,
            FileName = result.FileName,
            ChunkCount = result.ChunkCount,
        };
    }

    public async Task<List<KbDocumentResponse>> ListDocumentsAsync(CancellationToken ct = default)
    {
        var docs = await _aiClient.KbListDocumentsAsync(ct);
        if (docs == null)
            throw new ExternalApiException("Không lấy được danh sách tài liệu.");

        return docs.Select(d => new KbDocumentResponse
        {
            Id = d.Id,
            FileName = d.FileName,
            SourceType = d.SourceType,
            ChunkCount = d.ChunkCount,
            Status = d.Status,
            CreatedAt = d.CreatedAt,
        }).ToList();
    }

    public async Task DeleteDocumentAsync(string documentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Thiếu mã tài liệu.");

        var ok = await _aiClient.KbDeleteDocumentAsync(documentId, ct);
        if (!ok)
            throw new ExternalApiException("Xoá tài liệu thất bại.");
    }
}
