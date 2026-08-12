using MV.DomainLayer.DTO.RequestModel.Policy;
using MV.DomainLayer.DTO.RequestModel.Question;
using MV.DomainLayer.DTO.ResponseModel.Policy;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Văn bản pháp lý công khai. Trang công khai chỉ thấy bản `published`; CMS thấy cả nháp
/// và bản đã lưu trữ (xoá mềm).
/// </summary>
public interface IPolicyService
{
    // ── Công khai (không cần đăng nhập) ──

    Task<List<PolicyDocumentSummaryResponse>> GetPublishedAsync(CancellationToken ct = default);

    Task<PolicyDocumentResponse?> GetPublishedBySlugAsync(string slug, CancellationToken ct = default);

    // ── CMS ──

    /// <param name="includeArchived">Mặc định ẩn bản đã lưu trữ để danh sách đỡ rối.</param>
    Task<List<PolicyDocumentSummaryResponse>> GetAllAsync(bool includeArchived, CancellationToken ct = default);

    Task<PolicyDocumentResponse?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<PolicyDocumentResponse> CreateAsync(CreatePolicyDocumentRequest request, string? actorUserId, CancellationToken ct = default);

    Task<PolicyDocumentResponse> UpdateAsync(int id, UpdatePolicyDocumentRequest request, string? actorUserId, CancellationToken ct = default);

    /// <param name="publish">true = xuất bản, false = đưa về nháp (gỡ khỏi trang công khai).</param>
    Task<PolicyDocumentResponse> SetPublishedAsync(int id, bool publish, string? actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Gán lại thứ tự hiển thị cho cả danh sách — đây là thứ tự các văn bản xếp trên sidebar
    /// trang công khai. Nhận nguyên mảng để một lần kéo-thả chỉ tốn một request.
    /// </summary>
    Task ReorderAsync(ReorderRequest request, string? actorUserId, CancellationToken ct = default);

    /// <summary>Xoá mềm: chuyển sang `archived`. Không xoá cứng vì đây là văn bản người dùng đã đồng ý.</summary>
    Task ArchiveAsync(int id, string? actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Đưa văn bản đã lưu trữ trở lại dạng nháp. Cố ý KHÔNG khôi phục thẳng về `published`:
    /// văn bản vừa lấy khỏi kho lưu trữ cần được đọc lại rồi mới bấm xuất bản.
    /// </summary>
    Task<PolicyDocumentResponse> RestoreAsync(int id, string? actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Xoá cứng khỏi DB. Chỉ áp dụng cho văn bản đang ở trạng thái `archived` — buộc phải qua
    /// hai bước (lưu trữ rồi mới xoá) để không mất trắng một văn bản đang hiển thị chỉ vì bấm nhầm.
    /// </summary>
    Task DeleteAsync(int id, CancellationToken ct = default);
}
