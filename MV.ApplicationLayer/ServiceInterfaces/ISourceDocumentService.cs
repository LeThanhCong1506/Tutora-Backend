using Microsoft.AspNetCore.Http;
using MV.DomainLayer.DTO.ResponseModel.Question;
using MV.DomainLayer.DTO.RequestModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Upload PDF -> AI extract câu hỏi -> lưu vào question bank (pending_review).
/// </summary>
public interface ISourceDocumentService
{
    /// <summary>
    /// Xử lý PDF upload: validate ≤20 trang, lưu file + source_documents, gọi
    /// tutora-ai extract, lưu N câu (pending_review), embed từng câu.
    /// </summary>
    Task<UploadPdfResponse> UploadAndExtractAsync(
        IFormFile file,
        int? defaultSubjectId,
        int? defaultGradeLevelId,
        string? uploadedBy,
        CancellationToken ct = default);

    /// <summary>Lịch sử trích xuất — list các lần upload PDF (mới nhất trước), phân trang.</summary>
    Task<PagedList<SourceDocumentResponse>> GetHistoryAsync(
        int pageNumber, int pageSize, CancellationToken ct = default);

    /// <summary>Chi tiết 1 lần upload: file + các câu đã tách. Null nếu không thấy.</summary>
    Task<SourceDocumentDetailResponse?> GetDetailAsync(Guid id, CancellationToken ct = default);
}
