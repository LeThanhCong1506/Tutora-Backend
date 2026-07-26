using Microsoft.AspNetCore.Http;
using MV.DomainLayer.DTO.ResponseModel.KnowledgeBase;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Quản lý Knowledge Base Tutora (nội dung/chính sách CEO upload từ CMS để bot trả FAQ).
/// </summary>
public interface IKnowledgeBaseService
{
    /// <summary>Upload 1 file (pdf/docx/xlsx) → tutora-ai extract/chunk/embed vào KB.</summary>
    Task<KbUploadResponse> UploadAsync(IFormFile file, string? uploadedBy, CancellationToken ct = default);

    /// <summary>Danh sách tài liệu KB đã nạp (mới nhất trước).</summary>
    Task<List<KbDocumentResponse>> ListDocumentsAsync(CancellationToken ct = default);

    /// <summary>Xoá 1 tài liệu KB + toàn bộ chunk của nó.</summary>
    Task DeleteDocumentAsync(string documentId, CancellationToken ct = default);
}
