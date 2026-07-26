using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;

namespace MV.InfrastructureLayer.Repositories;

public class QuestionNoteRepository(AgoraDbContext context) : IQuestionNoteRepository
{
    public Task<List<QuestionNote>> GetByUserAsync(string userId, string? subject = null, int? gradeLevel = null)
        => context.QuestionNotes
            .Where(n => n.UserId == userId)
            .Where(n => string.IsNullOrEmpty(subject) || n.Subject == subject)
            .Where(n => gradeLevel == null || n.GradeLevel == gradeLevel)
            .OrderByDescending(n => n.CreatedAt)
            .AsNoTracking()
            .ToListAsync();

    public Task<QuestionNote?> FindByIdAsync(Guid noteId)
        => context.QuestionNotes.FirstOrDefaultAsync(n => n.NoteId == noteId);

    public Task<QuestionNote?> FindDuplicateAsync(string userId, Guid? sourceSessionId, string title)
        => context.QuestionNotes.FirstOrDefaultAsync(n =>
            n.UserId == userId && n.SourceSessionId == sourceSessionId && n.Title == title);

    public Task<List<string>> GetSavedTitlesBySessionAsync(string userId, Guid sourceSessionId)
        => context.QuestionNotes
            .Where(n => n.UserId == userId && n.SourceSessionId == sourceSessionId)
            .Select(n => n.Title)
            .AsNoTracking()
            .ToListAsync();

    public void Add(QuestionNote note) => context.QuestionNotes.Add(note);

    public void Update(QuestionNote note) => context.QuestionNotes.Update(note);

    public void Remove(QuestionNote note) => context.QuestionNotes.Remove(note);

    public Task<int> SaveChangesAsync() => context.SaveChangesAsync();
}
