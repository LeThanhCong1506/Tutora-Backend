using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Bài tập nhanh gia sư tạo TRONG BUỔI HỌC từ tài liệu của khoá.
/// Luồng: chọn tài liệu + prompt -> AI sinh nháp -> gia sư duyệt/sửa -> gửi ->
/// học sinh làm từng câu (trắc nghiệm phản hồi ngay, tự luận hỏi miệng gia sư).
/// </summary>
public interface ISessionPracticeService
{
    /// <summary>
    /// Danh sách bộ đề của booking. Gia sư thấy cả nháp; học sinh chỉ thấy bộ đã gửi
    /// và được ghép sẵn bài làm của chính em.
    /// </summary>
    Task<List<SessionPracticeSetResponse>> GetSetsAsync(int bookingId, string actorUserId);

    /// <summary>Gia sư bấm "Tạo câu hỏi" — gọi AI, lưu bộ nháp.</summary>
    Task<SessionPracticeSetResponse> GenerateAsync(int bookingId, string tutorUserId, GenerateSessionPracticeRequest request);

    /// <summary>Gia sư sửa 1 câu (chỉ khi bộ còn nháp).</summary>
    Task<SessionPracticeQuestionResponse> UpdateQuestionAsync(Guid questionId, string tutorUserId, UpdateSessionPracticeQuestionRequest request);

    /// <summary>Gia sư xoá 1 câu (chỉ khi bộ còn nháp).</summary>
    Task DeleteQuestionAsync(Guid questionId, string tutorUserId);

    /// <summary>
    /// Gia sư gửi RIÊNG 1 câu cho học sinh. Gửi lẻ chứ không gửi cả bộ: gia sư duyệt
    /// tới đâu gửi tới đó, câu chưa ưng vẫn sửa/xoá được.
    /// </summary>
    Task<SessionPracticeQuestionResponse> SendQuestionAsync(Guid questionId, string tutorUserId);

    /// <summary>Gửi mọi câu CHƯA gửi trong bộ — nút "gửi tất cả".</summary>
    Task<SessionPracticeSetResponse> SendAsync(Guid setId, string tutorUserId);

    /// <summary>Học sinh trả lời 1 câu. Trắc nghiệm chấm ngay; làm lại thì ghi đè.</summary>
    Task<SessionPracticeAnswerResponse> SubmitAnswerAsync(Guid questionId, string studentUserId, SubmitSessionPracticeAnswerRequest request);
}
