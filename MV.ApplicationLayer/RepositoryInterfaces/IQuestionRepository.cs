using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces;

public interface IQuestionRepository
{
    Task AddAsync(QuestionBank question);

    Task<QuestionBank?> GetByIdAsync(Guid id);

    /// <summary>List có phân trang + filter (môn/khối/chương/trạng thái/độ khó/lời giải) + sort.</summary>
    Task<PagedList<QuestionBank>> GetPagedAsync(
        int pageNumber,
        int pageSize,
        int? subjectId = null,
        int? gradeLevelId = null,
        IReadOnlyList<int>? chapterIds = null,
        string? reviewStatus = null,
        string? search = null,
        IReadOnlyList<string>? difficulties = null,
        bool? hasSolution = null,
        string? sortBy = null,
        string? sortDir = null,
        string? answerFormat = null);

    void Update(QuestionBank question);

    void Remove(QuestionBank question);
}
