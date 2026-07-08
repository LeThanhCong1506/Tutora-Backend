using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces;

public interface IQuestionRepository
{
    Task AddAsync(QuestionBank question);

    Task<QuestionBank?> GetByIdAsync(Guid id);

    /// <summary>List có phân trang + filter (môn/khối/chương/trạng thái/tìm content).</summary>
    Task<PagedList<QuestionBank>> GetPagedAsync(
        int pageNumber,
        int pageSize,
        int? subjectId = null,
        int? gradeLevelId = null,
        string? chapter = null,
        string? reviewStatus = null,
        string? search = null);

    void Update(QuestionBank question);

    void Remove(QuestionBank question);
}
