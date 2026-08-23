using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Điều phối việc dùng Gemini phân tích video buổi học: tóm tắt cho học sinh (+ chat hỏi tiếp)
/// và tự động điền báo cáo cho gia sư. Cả 2 dùng chung <c>ClassSessionAiJob</c> + có thể tái dùng
/// cùng 1 lần upload video lên Gemini (xem RunStudentSummaryJobAsync/RunTutorReportFillJobAsync).
/// </summary>
public interface IClassSessionVideoAiService
{
    /// <summary>Tóm tắt (+ chép lời) dựa trên MỌI video hiện có trong chuỗi buổi bù/phụ/học lại chứa
    /// classSessionId — 1 video thì tóm tắt đúng video đó, ≥2 video thì tự động hợp nhất. Job luôn lưu
    /// dưới id buổi GỐC của chuỗi nên trigger từ bất kỳ buổi nào trong chuỗi đều dùng chung 1 job.</summary>
    Task<ClassSessionAiJobResponse> TriggerStudentSummaryAsync(int classSessionId, string studentUserId, CancellationToken ct = default);
    Task<ClassSessionAiJobResponse> GetStudentSummaryStatusAsync(int classSessionId, string studentUserId, CancellationToken ct = default);
    Task<string> AskFollowUpAsync(int classSessionId, string studentUserId, string question, CancellationToken ct = default);
    Task<List<ClassSessionVideoChatMessageResponse>> GetFollowUpMessagesAsync(int classSessionId, string studentUserId, CancellationToken ct = default);

    Task<ClassSessionAiJobResponse> TriggerTutorReportFillAsync(int classSessionId, string tutorUserId, CancellationToken ct = default);
    Task<ClassSessionAiJobResponse> GetTutorReportFillStatusAsync(int classSessionId, string tutorUserId, CancellationToken ct = default);

    /// <summary>Hangfire job target. swallowFailure=true cho nhánh chạy nền (không để lỗi AI làm job fail vĩnh viễn).</summary>
    Task RunStudentSummaryJobAsync(Guid jobId, bool swallowFailure);

    /// <summary>Hangfire job target — chép lời chạy nền, do RunStudentSummaryJobAsync xếp hàng sau khi
    /// tóm tắt đã trả cho học sinh.</summary>
    Task RunStudentTranscriptJobAsync(Guid jobId, bool swallowFailure);

    /// <summary>Hangfire job target. swallowFailure=true cho nhánh chạy nền.</summary>
    Task RunTutorReportFillJobAsync(Guid jobId, bool swallowFailure);

    /// <summary>Hangfire job target — tóm tắt hợp nhất chuỗi (≥2 video). Tự tóm tắt + chép lời (và cache
    /// lại) mọi buổi trong chuỗi còn thiếu, rồi gọi 1 lượt Gemini text-only để hợp nhất phần tóm tắt.</summary>
    Task RunChainSummaryJobAsync(Guid jobId, bool swallowFailure);
}
