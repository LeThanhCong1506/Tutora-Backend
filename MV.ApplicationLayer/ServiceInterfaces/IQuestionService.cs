using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.RequestModel.Question;
using MV.DomainLayer.DTO.ResponseModel.Question;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// CRUD question bank cho staff/admin (CMS). Khi tạo/sửa content, service gọi
/// tutora-ai để embed (content -> vector) và lưu embedding vào cột pgvector.
/// </summary>
public interface IQuestionService
{
    Task<QuestionResponse> CreateAsync(CreateQuestionRequest request, string? createdBy, CancellationToken ct = default);

    Task<QuestionResponse?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<PagedList<QuestionResponse>> GetPagedAsync(
        int pageNumber, int pageSize,
        int? subjectId, int? gradeLevelId, string? chapter,
        string? reviewStatus, string? search,
        CancellationToken ct = default);

    /// <summary>Trả null nếu không tìm thấy câu hỏi.</summary>
    Task<QuestionResponse?> UpdateAsync(Guid id, UpdateQuestionRequest request, CancellationToken ct = default);

    /// <summary>Trả false nếu không tìm thấy.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
