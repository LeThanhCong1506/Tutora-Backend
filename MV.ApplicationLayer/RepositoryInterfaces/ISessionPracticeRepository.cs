using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces;

/// <summary>
/// Bài tập nhanh gia sư tạo TRONG BUỔI HỌC (practice_sets/questions/answers).
///
/// KHÁC <see cref="IPracticeRepository"/> — cái đó là vòng luyện tập sau khi giải bài
/// (practice_attempts, lấy câu từ question bank). Hai tính năng không liên quan nhau.
/// </summary>
public interface ISessionPracticeRepository
{
    /// <summary>Bộ đề của 1 booking, kèm câu hỏi + tài liệu nguồn.</summary>
    Task<List<SessionPracticeSet>> GetSetsByBookingAsync(int bookingId, bool sentOnly);

    Task<SessionPracticeSet?> GetSetAsync(Guid setId);

    Task<SessionPracticeQuestion?> GetQuestionAsync(Guid questionId);

    /// <summary>Bài làm của 1 học sinh cho các câu chỉ định — ghép vào response.</summary>
    Task<List<SessionPracticeAnswer>> GetAnswersAsync(string studentId, IReadOnlyCollection<Guid> questionIds);

    Task<SessionPracticeAnswer?> GetAnswerAsync(Guid questionId, string studentId);

    /// <summary>Nội dung đã trích của các tài liệu — dựng prompt cho AI.</summary>
    Task<List<LearningMaterialContent>> GetMaterialContentsAsync(IReadOnlyCollection<int> materialIds);

    Task<LearningMaterialContent?> GetMaterialContentAsync(int materialId);

    /// <summary>
    /// Gắn tài liệu nguồn vào bộ qua bảng nối, dùng ID thay vì attach entity —
    /// entity lấy AsNoTracking mà attach vào là EF cố INSERT lại tài liệu đã có.
    /// </summary>
    Task LinkMaterialsAsync(Guid setId, IReadOnlyCollection<int> materialIds);

    void AddSet(SessionPracticeSet set);
    void AddAnswer(SessionPracticeAnswer answer);
    void AddMaterialContent(LearningMaterialContent content);
    void RemoveQuestion(SessionPracticeQuestion question);

    Task<int> SaveChangesAsync();
}
