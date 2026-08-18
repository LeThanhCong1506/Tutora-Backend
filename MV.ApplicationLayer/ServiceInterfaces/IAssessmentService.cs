using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.RequestModel.Assessment;
using MV.DomainLayer.DTO.ResponseModel.Assessment;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>CRUD bộ đề đánh giá (Admin-only). Câu hỏi ở bảng riêng, không vào pool RAG.</summary>
public interface IAssessmentService
{
    Task<AssessmentResponse> CreateAsync(CreateAssessmentRequest request, string? createdBy, CancellationToken ct = default);

    Task<PagedList<AssessmentResponse>> GetPagedAsync(
        int pageNumber, int pageSize,
        int? subjectId, int? gradeLevelId, string? status, string? search,
        string? sortBy, string? sortDir,
        CancellationToken ct = default);

    /// <summary>Chi tiết đề kèm câu hỏi.</summary>
    Task<AssessmentDetailResponse?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Null nếu không tìm thấy đề.</summary>
    Task<AssessmentResponse?> UpdateAsync(Guid id, UpdateAssessmentRequest request, CancellationToken ct = default);

    /// <summary>Đổi trạng thái. (null, lỗi) nếu không được phép — vd phát hành đề thiếu câu.</summary>
    Task<(AssessmentResponse? Result, string? Error)> UpdateStatusAsync(
        Guid id, string status, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    // Câu hỏi trong đề
    /// <summary>Thêm câu vào đề.</summary>
    Task<(AssessmentQuestionResponse? Result, string? Error)> AddQuestionAsync(
        Guid assessmentId, CreateAssessmentQuestionRequest request, CancellationToken ct = default);

    Task<(AssessmentQuestionResponse? Result, string? Error)> UpdateQuestionAsync(
        Guid assessmentId, Guid questionId, UpdateAssessmentQuestionRequest request, CancellationToken ct = default);

    /// <summary>Xoá câu và dồn lại thứ tự.</summary>
    Task<bool> DeleteQuestionAsync(Guid assessmentId, Guid questionId, CancellationToken ct = default);

    /// <summary>Sắp lại thứ tự theo danh sách id.</summary>
    Task<(bool Ok, string? Error)> ReorderQuestionsAsync(
        Guid assessmentId, IReadOnlyList<Guid> questionIds, CancellationToken ct = default);
}
