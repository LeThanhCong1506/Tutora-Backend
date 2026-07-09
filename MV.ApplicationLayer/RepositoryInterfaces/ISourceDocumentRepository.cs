using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces;

public interface ISourceDocumentRepository
{
    Task AddAsync(SourceDocument document);

    Task<SourceDocument?> GetByIdAsync(Guid id);

    void Update(SourceDocument document);

    /// <summary>Thêm nhiều câu hỏi cùng lúc (kết quả extract từ PDF).</summary>
    Task AddQuestionsAsync(IEnumerable<QuestionBank> questions);
}
