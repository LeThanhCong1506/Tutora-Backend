using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.RepositoryInterfaces;
using static MV.ApplicationLayer.ServiceInterfaces.ITutorAiClient;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO.RequestModel.Practice;
using MV.DomainLayer.DTO.ResponseModel.Practice;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Vòng luyện tập sau khi giải bài.
///
/// Dạng TỰ LUẬN: mời câu cùng chương, học sinh tự làm rồi đối chiếu lời giải mẫu và tự
/// chấm. 
/// </summary>
public class PracticeService(IPracticeRepository repo, ITutorAiClient aiClient, ILogger<PracticeService> logger) : IPracticeService
{
    public async Task<PracticeQuestionResponse?> GetNextAsync(
        string userId, string? chapter, string? questionText, string? difficulty,
        CancellationToken ct = default)
    {
        // Bài đã làm rồi thì không mời lại — luyện là để gặp cái mới.
        var doneIds = await repo.GetAttemptedQuestionIdsAsync(userId);

        // Ưu tiên tìm bằng EMBEDDING: cùng chương chưa đủ, một chương có đủ mọi dạng
        if (!string.IsNullOrWhiteSpace(questionText))
        {
            var similar = await aiClient.FindSimilarQuestionsAsync(
                questionText, chapter, difficulty, doneIds, topK: 1, ct);

            if (similar.Count > 0)
            {
                var s = similar[0];
                return new PracticeQuestionResponse
                {
                    QuestionId = s.Id,
                    Content = s.Content,
                    Solution = s.Solution,
                    ChapterName = s.Chapter,
                    Difficulty = s.Difficulty,
                };
            }
        }

        // AI lỗi hoặc không có đề bài -> lùi về lọc theo chương như cũ.
        if (string.IsNullOrWhiteSpace(chapter)) return null;
        var candidates = await repo.GetPracticeCandidatesAsync(chapter, doneIds, 20);
        if (candidates.Count == 0) return null;

        var picked = candidates[Random.Shared.Next(candidates.Count)];
        return new PracticeQuestionResponse
        {
            QuestionId = picked.Id,
            Content = picked.Content,
            Solution = picked.Solution,
            ChapterName = picked.Chapter,
            Difficulty = picked.Difficulty,
        };
    }

    public async Task<PracticeResultResponse?> SubmitAsync(
        string userId, SubmitPracticeRequest request, CancellationToken ct = default)
    {
        var question = await repo.FindQuestionAsync(request.QuestionId);
        if (question is null) return null;

        var self = (request.SelfAssessment ?? "").Trim().ToLowerInvariant();
        if (self is not ("correct" or "partial" or "wrong")) return null;
        // 'partial' KHÔNG tính là làm được — hồ sơ chỉ nên tin lượt em tự nhận đúng hẳn.
        var isCorrect = self == "correct";

        repo.AddAttempt(new PracticeAttempt
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            QuestionId = question.Id,
            Chapter = question.Chapter,
            GradeLevelId = question.GradeLevelId,
            Difficulty = question.Difficulty,
            SelfAssessment = self,
            IsCorrect = isCorrect,
            SourceSessionId = request.SourceSessionId,
        });

        try
        {
            await repo.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Ghi hỏng thì vẫn trả kết quả cho học sinh — mất 1 tín hiệu còn hơn mất bài luyện.
            logger.LogWarning(ex, "Không ghi được lượt luyện (user {UserId})", userId);
        }

        return new PracticeResultResponse();
    }
}
