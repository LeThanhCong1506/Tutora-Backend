using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces;

public interface IPracticeRepository
{
    /// <summary>Id các câu học sinh đã luyện — không mời lại.</summary>
    Task<List<Guid>> GetAttemptedQuestionIdsAsync(string userId);

    /// <summary>Câu trắc nghiệm ĐÃ DUYỆT + có đáp án của 1 chương, trừ những câu đã làm.</summary>
    Task<List<QuestionBank>> GetPracticeCandidatesAsync(string chapter, List<Guid> excludeIds, int take);

    Task<QuestionBank?> FindQuestionAsync(Guid questionId);

    void AddAttempt(PracticeAttempt attempt);

    /// <summary>Đếm (đúng, tổng) theo chương để UI nói "em đã làm 3/5 câu đúng".</summary>
    Task<(int Correct, int Total)> CountByChapterAsync(string userId, string? chapter);

    Task<int> SaveChangesAsync();
}
