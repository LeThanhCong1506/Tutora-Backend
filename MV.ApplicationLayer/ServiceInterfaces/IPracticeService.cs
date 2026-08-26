using MV.DomainLayer.DTO.RequestModel.Practice;
using MV.DomainLayer.DTO.ResponseModel.Practice;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IPracticeService
{
    /// <summary>
    /// Mời 1 bài luyện TƯƠNG TỰ bài học sinh vừa hỏi (tìm bằng embedding qua tutora-ai).
    /// Null = không tìm được bài phù hợp, hoặc học sinh đã làm hết.
    /// </summary>
    Task<PracticeQuestionResponse?> GetNextAsync(
        string userId, string? chapter, string? questionText, string? difficulty,
        CancellationToken ct = default);

    /// <summary>Chấm câu luyện và ghi lại kết quả. Null = câu không tồn tại / không chấm được.</summary>
    Task<PracticeResultResponse?> SubmitAsync(string userId, SubmitPracticeRequest request, CancellationToken ct = default);
}
