using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces;

/// <summary>assessments / assessment_questions / attempts / proficiency.</summary>
public interface IAssessmentRepository
{
    Task AddAsync(Assessment assessment);

    /// <summary>Đề kèm câu theo display_order.</summary>
    Task<Assessment?> GetByIdWithQuestionsAsync(Guid id);

    /// <summary>Đề không kèm câu.</summary>
    Task<Assessment?> GetByIdAsync(Guid id);

    /// <summary>Phân trang + filter + search.</summary>
    Task<PagedList<Assessment>> GetPagedAsync(
        int pageNumber,
        int pageSize,
        int? subjectId = null,
        int? gradeLevelId = null,
        string? status = null,
        string? search = null,
        string? sortBy = null,
        string? sortDir = null);

    void Update(Assessment assessment);

    void Remove(Assessment assessment);

    /// <summary>Số câu + tổng điểm nhiều đề trong 1 query, tránh N+1.</summary>
    Task<Dictionary<Guid, (int Count, decimal TotalPoints)>> GetQuestionStatsAsync(IReadOnlyList<Guid> assessmentIds);

    // Câu hỏi trong đề
    Task AddQuestionAsync(AssessmentQuestion question);

    /// <summary>1 câu kèm nav để có Name.</summary>
    Task<AssessmentQuestion?> GetQuestionByIdAsync(Guid questionId);

    /// <summary>Câu của 1 đề theo display_order.</summary>
    Task<List<AssessmentQuestion>> GetQuestionsByAssessmentAsync(Guid assessmentId);

    /// <summary>display_order lớn nhất; 0 nếu chưa có câu.</summary>
    Task<int> GetMaxDisplayOrderAsync(Guid assessmentId);

    void UpdateQuestion(AssessmentQuestion question);

    void RemoveQuestion(AssessmentQuestion question);

    /// <summary>Null nếu id không tồn tại hoặc đã ngừng dùng.</summary>
    Task<string?> GetQuestionTypeSlugAsync(int questionTypeId);

    // Học sinh làm bài

    Task AddAttemptAsync(AssessmentAttempt attempt);

    /// <summary>Bài đang làm dở với 1 đề.</summary>
    Task<AssessmentAttempt?> GetInProgressAttemptAsync(Guid assessmentId, string userId);

    /// <summary>Bài làm kèm câu trả lời + câu hỏi gốc.</summary>
    Task<AssessmentAttempt?> GetAttemptWithAnswersAsync(Guid attemptId);

    /// <summary>Bài làm trơn, không nav.</summary>
    Task<AssessmentAttempt?> GetAttemptAsync(Guid attemptId);

    /// <summary>Lịch sử, mới nhất trước.</summary>
    Task<PagedList<AssessmentAttempt>> GetAttemptsByUserAsync(
        string userId, int pageNumber, int pageSize, int? subjectId = null);

    /// <summary>Số lần đã nộp của 1 môn.</summary>
    Task<int> CountSubmittedAttemptsAsync(string userId, int subjectId);

    Task AddAttemptAnswerAsync(AssessmentAttemptAnswer answer);

    void UpdateAttempt(AssessmentAttempt attempt);

    void UpdateAttemptAnswer(AssessmentAttemptAnswer answer);

    void RemoveAttemptAnswers(IEnumerable<AssessmentAttemptAnswer> answers);

    // Profile trình độ

    /// <summary>Profile theo môn.</summary>
    Task<StudentProficiencyProfile?> GetProficiencyProfileAsync(string userId, int subjectId);

    /// <summary>Mọi profile của học sinh — AI giải bài nạp cái này.</summary>
    Task<List<StudentProficiencyProfile>> GetProficiencyProfilesAsync(string userId);

    Task AddProficiencyProfileAsync(StudentProficiencyProfile profile);

    void UpdateProficiencyProfile(StudentProficiencyProfile profile);
}
