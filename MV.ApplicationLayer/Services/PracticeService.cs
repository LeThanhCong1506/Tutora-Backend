using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO.RequestModel.Practice;
using MV.DomainLayer.DTO.ResponseModel.Practice;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Vòng luyện tập sau khi giải bài.
///
/// Mục đích kép: chống chép bài (giải xong phải tự làm lại), và sinh tín hiệu đúng/sai
/// khách quan cho hồ sơ trình độ. Chấm bằng SO KHỚP CHUỖI với correct_answer, không gọi
/// LLM — nhãn phải khách quan thì hồ sơ mới đáng tin.
/// </summary>
public class PracticeService(IPracticeRepository repo, ILogger<PracticeService> logger) : IPracticeService
{
    public async Task<PracticeQuestionResponse?> GetNextAsync(
        string userId, string? chapter, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(chapter)) return null;

        // Câu đã làm rồi thì không mời lại — luyện là để gặp cái mới.
        var doneIds = await repo.GetAttemptedQuestionIdsAsync(userId);
        var candidates = await repo.GetPracticeCandidatesAsync(chapter, doneIds, 20);

        if (candidates.Count == 0) return null;

        // Random trong số ứng viên để hai học sinh cùng chương không nhận cùng một câu.
        var picked = candidates[Random.Shared.Next(candidates.Count)];

        return new PracticeQuestionResponse
        {
            QuestionId = picked.Id,
            Content = picked.Content,
            Options = (picked.AnswerOptions ?? new())
                .Select(o => new PracticeOptionResponse { Key = o.Key, Text = o.Text })
                .ToList(),
            ChapterName = picked.Chapter,
            Difficulty = picked.Difficulty,
        };
    }

    public async Task<PracticeResultResponse?> SubmitAsync(
        string userId, SubmitPracticeRequest request, CancellationToken ct = default)
    {
        var question = await repo.FindQuestionAsync(request.QuestionId);
        if (question?.CorrectAnswer is null) return null;

        // So khớp chuỗi, bỏ hoa/thường và khoảng trắng. Bỏ trống -> sai.
        var given = request.GivenAnswer?.Trim();
        var isCorrect = !string.IsNullOrEmpty(given)
            && string.Equals(given, question.CorrectAnswer.Trim(), StringComparison.OrdinalIgnoreCase);

        repo.AddAttempt(new PracticeAttempt
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            QuestionId = question.Id,
            Chapter = question.Chapter,
            GradeLevelId = question.GradeLevelId,
            Difficulty = question.Difficulty,
            GivenAnswer = given,
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

        var (chapterCorrect, chapterTotal) = await repo.CountByChapterAsync(userId, question.Chapter);

        return new PracticeResultResponse
        {
            IsCorrect = isCorrect,
            CorrectAnswer = question.CorrectAnswer,
            Solution = question.Solution,
            Explanation = question.Explanation,
            ChapterCorrect = chapterCorrect,
            ChapterTotal = chapterTotal,
        };
    }
}
