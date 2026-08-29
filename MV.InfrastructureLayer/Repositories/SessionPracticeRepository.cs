using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;

namespace MV.InfrastructureLayer.Repositories;

public class SessionPracticeRepository(AgoraDbContext context) : ISessionPracticeRepository
{
    public Task<List<SessionPracticeSet>> GetSetsByBookingAsync(int bookingId, bool sentOnly)
    {
        var query = context.SessionPracticeSets
            .AsNoTracking()
            .Include(s => s.Questions.OrderBy(q => q.DisplayOrder))
            .Include(s => s.Materials)
            .Where(s => s.BookingId == bookingId);

        // Học sinh chỉ thấy bộ đã gửi — bộ nháp là bản gia sư đang duyệt.
        if (sentOnly)
            query = query.Where(s => s.Status == SessionPracticeSetStatus.Sent);

        return query.OrderByDescending(s => s.CreatedAt).ToListAsync();
    }

    public Task<SessionPracticeSet?> GetSetAsync(Guid setId)
        => context.SessionPracticeSets
            .Include(s => s.Questions.OrderBy(q => q.DisplayOrder))
            .Include(s => s.Materials)
            .FirstOrDefaultAsync(s => s.Id == setId);

    public Task<SessionPracticeQuestion?> GetQuestionAsync(Guid questionId)
        => context.SessionPracticeQuestions
            .Include(q => q.Set)
            .FirstOrDefaultAsync(q => q.Id == questionId);

    public Task<List<SessionPracticeAnswer>> GetAnswersAsync(string studentId, IReadOnlyCollection<Guid> questionIds)
        => context.SessionPracticeAnswers
            .AsNoTracking()
            .Where(a => a.StudentId == studentId && questionIds.Contains(a.QuestionId))
            .ToListAsync();

    public Task<SessionPracticeAnswer?> GetAnswerAsync(Guid questionId, string studentId)
        => context.SessionPracticeAnswers
            .FirstOrDefaultAsync(a => a.QuestionId == questionId && a.StudentId == studentId);

    public Task<List<LearningMaterialContent>> GetMaterialContentsAsync(IReadOnlyCollection<int> materialIds)
        => context.LearningMaterialContents
            .AsNoTracking()
            .Where(c => materialIds.Contains(c.MaterialId))
            .ToListAsync();

    public Task<LearningMaterialContent?> GetMaterialContentAsync(int materialId)
        => context.LearningMaterialContents.FirstOrDefaultAsync(c => c.MaterialId == materialId);

    public async Task LinkMaterialsAsync(Guid setId, IReadOnlyCollection<int> materialIds)
    {
        // Lấy bản ghi ĐANG ĐƯỢC TRACK từ context để EF hiểu là quan hệ với bản ghi có
        // sẵn, không phải tài liệu mới.
        var tracked = await context.Learningmaterials
            .Where(m => materialIds.Contains(m.Materialid))
            .ToListAsync();

        var set = await context.SessionPracticeSets
            .Include(s => s.Materials)
            .FirstOrDefaultAsync(s => s.Id == setId);

        // Bộ vừa Add nhưng chưa SaveChanges -> chưa query ra được; lấy từ change tracker.
        set ??= context.ChangeTracker.Entries<SessionPracticeSet>()
            .Select(e => e.Entity)
            .FirstOrDefault(s => s.Id == setId);

        if (set == null)
            return;

        foreach (var material in tracked)
        {
            if (!set.Materials.Any(m => m.Materialid == material.Materialid))
                set.Materials.Add(material);
        }
    }

    public void AddSet(SessionPracticeSet set) => context.SessionPracticeSets.Add(set);

    public void AddAnswer(SessionPracticeAnswer answer) => context.SessionPracticeAnswers.Add(answer);

    public void AddMaterialContent(LearningMaterialContent content) => context.LearningMaterialContents.Add(content);

    public void RemoveQuestion(SessionPracticeQuestion question) => context.SessionPracticeQuestions.Remove(question);

    public Task<int> SaveChangesAsync() => context.SaveChangesAsync();
}
