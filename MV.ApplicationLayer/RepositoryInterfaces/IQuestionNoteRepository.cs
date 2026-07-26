using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces;

public interface IQuestionNoteRepository
{
    Task<List<QuestionNote>> GetByUserAsync(string userId, string? subject = null, int? gradeLevel = null);
    Task<QuestionNote?> FindByIdAsync(Guid noteId);
    /// <summary>Tìm note đã lưu cho cùng (user, session, title) — chống lưu trùng 1 version.</summary>
    Task<QuestionNote?> FindDuplicateAsync(string userId, Guid? sourceSessionId, string title);
    /// <summary>Các title đã lưu Note trong 1 session — FE gắn cờ NoteSaved cho từng canvas.</summary>
    Task<List<string>> GetSavedTitlesBySessionAsync(string userId, Guid sourceSessionId);
    void Add(QuestionNote note);
    void Update(QuestionNote note);
    void Remove(QuestionNote note);
    Task<int> SaveChangesAsync();
}
