using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.DTO.ResponseModel.Question;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    /// <summary>
    /// Dữ liệu tra cứu dùng chung (public) cho FE đổ dropdown: môn học, khối lớp,
    /// chương (theo môn+lớp), loại câu hỏi.
    /// </summary>
    public interface ILookupService
    {
        Task<List<SubjectResponse>> GetSubjectsAsync();
        Task<List<GradeLevelResponse>> GetGradeLevelsAsync();

        /// <summary>Chương theo môn+lớp (null = tất cả). Chỉ chương active.</summary>
        Task<List<ChapterResponse>> GetChaptersAsync(int? subjectId, int? gradeLevelId);

        Task<List<QuestionTypeResponse>> GetQuestionTypesAsync();
    }
}
