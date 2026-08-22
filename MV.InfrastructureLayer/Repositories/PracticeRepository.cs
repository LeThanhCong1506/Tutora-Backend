using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;

namespace MV.InfrastructureLayer.Repositories;

public class PracticeRepository(AgoraDbContext context) : IPracticeRepository
{
    public Task<List<Guid>> GetAttemptedQuestionIdsAsync(string userId)
        => context.PracticeAttempts
            .Where(p => p.UserId == userId)
            .Select(p => p.QuestionId)
            .ToListAsync();

    public Task<List<QuestionBank>> GetPracticeCandidatesAsync(string chapter, List<Guid> excludeIds, int take)
        => context.QuestionBanks
            .Where(q => q.ReviewStatus == "published"
                        && q.AnswerFormat == "mc"
                        && q.CorrectAnswer != null
                        && q.Chapter == chapter
                        && !excludeIds.Contains(q.Id))
            .Take(take)
            .ToListAsync();

    public Task<QuestionBank?> FindQuestionAsync(Guid questionId)
        => context.QuestionBanks.FirstOrDefaultAsync(q => q.Id == questionId);

    public void AddAttempt(PracticeAttempt attempt)
        => context.PracticeAttempts.Add(attempt);

    public async Task<(int Correct, int Total)> CountByChapterAsync(string userId, string? chapter)
    {
        var rows = await context.PracticeAttempts
            .Where(p => p.UserId == userId && p.Chapter == chapter)
            .Select(p => p.IsCorrect)
            .ToListAsync();
        return (rows.Count(x => x), rows.Count);
    }

    public Task<int> SaveChangesAsync() => context.SaveChangesAsync();
}
