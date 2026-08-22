using MV.DomainLayer.DTO.RequestModel.Practice;
using MV.DomainLayer.DTO.ResponseModel.Practice;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IPracticeService
{
    /// <summary>
    /// Mời 1 câu luyện cùng chương với bài học sinh vừa hỏi.
    /// Null = chương đó chưa có câu nào chấm được, hoặc học sinh đã làm hết.
    /// </summary>
    Task<PracticeQuestionResponse?> GetNextAsync(string userId, string? chapter, CancellationToken ct = default);

    /// <summary>Chấm câu luyện và ghi lại kết quả. Null = câu không tồn tại / không chấm được.</summary>
    Task<PracticeResultResponse?> SubmitAsync(string userId, SubmitPracticeRequest request, CancellationToken ct = default);
}
