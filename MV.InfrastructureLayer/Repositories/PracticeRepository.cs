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
            // Cần solution để học sinh đối chiếu; KHÔNG cần correct_answer nữa vì
            // luyện dạng tự luận -> kho rộng hơn hẳn (mọi câu trong bank đều có solution).
            .Where(q => q.ReviewStatus == "published"
                        && q.Solution != null
                        && q.Chapter == chapter
                        && !excludeIds.Contains(q.Id))
            .Take(take)
            .ToListAsync();

    public Task<QuestionBank?> FindQuestionAsync(Guid questionId)
        => context.QuestionBanks.FirstOrDefaultAsync(q => q.Id == questionId);

    public void AddAttempt(PracticeAttempt attempt)
        => context.PracticeAttempts.Add(attempt);

    public Task<int> SaveChangesAsync() => context.SaveChangesAsync();
}
