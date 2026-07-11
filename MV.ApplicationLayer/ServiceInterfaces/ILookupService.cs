using MV.DomainLayer.DTO.RequestModel.Question;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.DTO.ResponseModel.Question;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    /// <summary>
    /// </summary>
    public interface ILookupService
    {
        Task<List<SubjectResponse>> GetSubjectsAsync();
        Task<List<GradeLevelResponse>> GetGradeLevelsAsync();

        /// <summary>Danh sách thứ trong tuần (sắp theo DayOrder). Read-only.</summary>
        Task<List<DayOfWeekResponse>> GetDaysOfWeekAsync();

        /// <summary>Chương theo môn+lớp (null = tất cả). Chỉ chương active.</summary>
        Task<List<ChapterResponse>> GetChaptersAsync(int? subjectId, int? gradeLevelId);

        Task<List<QuestionTypeResponse>> GetQuestionTypesAsync();

        // Subject
        Task<SubjectResponse> CreateSubjectAsync(SubjectRequest req);
        Task<SubjectResponse?> UpdateSubjectAsync(int id, SubjectRequest req);
        Task<bool> DeleteSubjectAsync(int id);

        // GradeLevel
        Task<GradeLevelResponse> CreateGradeLevelAsync(GradeLevelRequest req);
        Task<GradeLevelResponse?> UpdateGradeLevelAsync(int id, GradeLevelRequest req);
        Task<bool> DeleteGradeLevelAsync(int id);

        // Chapter
        Task<ChapterResponse> CreateChapterAsync(ChapterRequest req);
        Task<ChapterResponse?> UpdateChapterAsync(int id, ChapterRequest req);
        Task<bool> DeleteChapterAsync(int id);

        // QuestionType
        Task<QuestionTypeResponse> CreateQuestionTypeAsync(QuestionTypeRequest req);
        Task<QuestionTypeResponse?> UpdateQuestionTypeAsync(int id, QuestionTypeRequest req);
        Task<bool> DeleteQuestionTypeAsync(int id);
    }

    /// <summary>
    /// Ném khi cố xoá danh mục (môn/lớp/chương/loại câu) đang được câu hỏi tham chiếu.
    /// Controller map -> HTTP 409 Conflict.
    /// </summary>
    public class LookupInUseException : Exception
    {
        public LookupInUseException(string message) : base(message) { }
    }
}
