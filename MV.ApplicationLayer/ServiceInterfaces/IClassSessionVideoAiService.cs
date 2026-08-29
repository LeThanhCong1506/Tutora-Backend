using Hangfire;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Điều phối việc dùng Gemini phân tích video buổi học: tóm tắt cho học sinh (+ chat hỏi tiếp)
/// và tự động điền báo cáo cho gia sư. Cả 2 dùng chung <c>ClassSessionAiJob</c> + có thể tái dùng
/// cùng 1 lần upload video lên Gemini (xem RunStudentSummaryJobAsync/RunTutorReportFillJobAsync).
///
/// Hàng đợi Hangfire: [Queue] ở đây (không phải trên class implement) vì BackgroundJobClient.Enqueue
/// được gọi qua kiểu interface (Enqueue&lt;IClassSessionVideoAiService&gt;(s =&gt; ...)) — Hangfire đọc
/// attribute từ MethodInfo/Type suy ra được từ expression, tức là của interface này.
/// "interactive" = job người dùng bấm và đang chờ kết quả ngay, ưu tiên chạy trước. "bulk" = job chạy
/// nền không ai chờ (transcript dài, tổng hợp chuỗi, làm nóng cache) — xem Program.cs, Queues xếp theo
/// thứ tự ["interactive", "default", "bulk"] nên "interactive" luôn được rút trước nếu có việc.
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
    [Queue("interactive")]
    Task RunStudentSummaryJobAsync(Guid jobId, bool swallowFailure);

    /// <summary>Hangfire job target — chép lời chạy nền, do RunStudentSummaryJobAsync xếp hàng sau khi
    /// tóm tắt đã trả cho học sinh. Không ai đang chờ kết quả trực tiếp nên xếp queue "bulk".</summary>
    [Queue("bulk")]
    Task RunStudentTranscriptJobAsync(Guid jobId, bool swallowFailure);

    /// <summary>Hangfire job target. swallowFailure=true cho nhánh chạy nền.</summary>
    [Queue("interactive")]
    Task RunTutorReportFillJobAsync(Guid jobId, bool swallowFailure);

    /// <summary>Hangfire job target — tóm tắt hợp nhất chuỗi (≥2 video). Tự tóm tắt + chép lời (và cache
    /// lại) mọi buổi trong chuỗi còn thiếu, rồi gọi 1 lượt Gemini text-only để hợp nhất phần tóm tắt.
    /// Có thể chạy rất lâu (nhiều buổi × tải/transcode/upload) nên xếp queue "bulk".</summary>
    [Queue("bulk")]
    Task RunChainSummaryJobAsync(Guid jobId, bool swallowFailure);

    /// <summary>Hangfire job target — làm nóng cache GeminiFileUri cho 1 buổi học ngay khi video vừa relay
    /// xong lên Drive (gọi từ RecordingRelayService), trước khi có ai bấm tóm tắt/điền báo cáo. Best-effort:
    /// lỗi ở đây không ảnh hưởng gì — job tóm tắt/điền báo cáo thật sẽ tự upload lại nếu cache chưa kịp có.
    /// Chạy nền, không ai chờ, nên xếp queue "bulk".</summary>
    [Queue("bulk")]
    Task PrewarmGeminiFileAsync(int classSessionId);
}
