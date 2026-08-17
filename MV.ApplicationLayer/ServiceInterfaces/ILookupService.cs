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

        // Admin: liệt kê TẤT CẢ (gồm mục đã ngừng dùng) để quản lý.
        Task<List<SubjectResponse>> GetAllSubjectsAsync();
        Task<List<GradeLevelResponse>> GetAllGradeLevelsAsync();

        /// <summary>Chương cho CMS: gồm cả đã ngừng dùng, kèm tên môn/khối và số câu hỏi tham chiếu.</summary>
        Task<List<AdminChapterResponse>> GetAllChaptersAsync(int? subjectId, int? gradeLevelId);

        /// <summary>Loại câu hỏi cho CMS: gồm cả đã ngừng dùng, kèm số câu hỏi tham chiếu.</summary>
        Task<List<AdminQuestionTypeResponse>> GetAllQuestionTypesAsync();

        /// <summary>
        /// Sắp xếp lại DisplayOrder
        /// </summary>
        Task<bool> ReorderSubjectsAsync(ReorderRequest req);
        Task<bool> ReorderGradeLevelsAsync(ReorderRequest req);
        Task<bool> ReorderChaptersAsync(ReorderRequest req);
        Task<bool> ReorderQuestionTypesAsync(ReorderRequest req);

        // Subject
        Task<SubjectResponse> CreateSubjectAsync(SubjectRequest req);
        Task<SubjectResponse?> UpdateSubjectAsync(int id, SubjectRequest req);
        Task<LookupDeleteResult> DeleteSubjectAsync(int id);
        Task<bool> HardDeleteSubjectAsync(int id);

        // GradeLevel
        Task<GradeLevelResponse> CreateGradeLevelAsync(GradeLevelRequest req);
        Task<GradeLevelResponse?> UpdateGradeLevelAsync(int id, GradeLevelRequest req);
        Task<LookupDeleteResult> DeleteGradeLevelAsync(int id);

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

    /// <summary>
    /// Kết quả soft-delete Subject/GradeLevel: có tìm thấy không, và đang có bao nhiêu
    /// Tutorsubjectgradeprice active tham chiếu (chỉ để cảnh báo Admin/Staff, không chặn xoá).
    /// </summary>
    public record LookupDeleteResult(bool Found, int AffectedCount);
}
