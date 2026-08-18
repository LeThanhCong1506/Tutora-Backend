using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.RequestModel.Assessment;
using MV.DomainLayer.DTO.ResponseModel.Assessment;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Học sinh làm đề + kết quả. BE chấm khách quan, không kết luận đạt/không đạt; dữ kiện thô
/// đi hết cho AI phân tích rồi lưu vào profile trình độ.
/// </summary>
public interface IAssessmentAttemptService
{
    /// <summary>Đề học sinh làm được, theo môn/lớp. Rỗng nếu chưa admin phát hành đề nào.</summary>
    Task<List<AvailableAssessmentResponse>> GetAvailableAsync(
        int? subjectId, int? gradeLevelId, CancellationToken ct = default);

    /// <summary>
    /// Chọn 1 đề ngẫu nhiên khớp môn/lớp rồi bắt đầu luôn. Đề chưa có cột cấp độ nên
    /// tiêu chí chỉ là môn + lớp, còn lại random.
    /// </summary>
    Task<(AttemptInProgressResponse? Result, string? Error)> StartRandomAsync(
        int subjectId, int? gradeLevelId, string userId, CancellationToken ct = default);

    /// <summary>Bắt đầu/tiếp tục làm đề. Còn bài dở thì trả lại bài đó, không tạo mới.</summary>
    Task<(AttemptInProgressResponse? Result, string? Error)> StartAsync(
        Guid assessmentId, string userId, CancellationToken ct = default);

    /// <summary>Nộp bài: chấm từng câu rồi đặt analysis_status = pending cho AI.</summary>
    Task<(AttemptResultResponse? Result, string? Error)> SubmitAsync(
        Guid attemptId, string userId, SubmitAttemptRequest request, CancellationToken ct = default);

    /// <summary>Kết quả 1 bài, kèm chi tiết từng câu.</summary>
    Task<AttemptResultResponse?> GetResultAsync(Guid attemptId, string userId, CancellationToken ct = default);

    /// <summary>Lịch sử làm bài, không kèm chi tiết câu.</summary>
    Task<PagedList<AttemptResultResponse>> GetHistoryAsync(
        string userId, int pageNumber, int pageSize, int? subjectId, CancellationToken ct = default);

    /// <summary>Dữ kiện thô gửi AI phân tích. Null nếu bài chưa nộp.</summary>
    Task<AttemptAnalysisInputResponse?> GetAnalysisInputAsync(Guid attemptId, CancellationToken ct = default);

    /// <summary>Ghi kết quả AI + ghi đè profile trình độ của (học sinh, môn).</summary>
    Task<bool> SaveAnalysisAsync(Guid attemptId, SaveAnalysisRequest request, CancellationToken ct = default);

    /// <summary>
    /// Chạy trọn phân tích: dữ kiện thô -> tutora-ai -> ghi profile. Null cả 2 = không
    /// tìm thấy bài đã nộp.
    /// </summary>
    Task<(AttemptAnalysisResultResponse? Result, string? Error)> RunAnalysisAsync(
        Guid attemptId, CancellationToken ct = default);

    /// <summary>AI lỗi — bài vẫn giữ điểm, retry được.</summary>
    Task<bool> MarkAnalysisFailedAsync(Guid attemptId, string error, CancellationToken ct = default);

    /// <summary>Profile trình độ — AI giải bài nạp vào prompt. subjectId null = mọi môn.</summary>
    Task<List<ProficiencyProfileResponse>> GetProficiencyAsync(
        string userId, int? subjectId, CancellationToken ct = default);
}
