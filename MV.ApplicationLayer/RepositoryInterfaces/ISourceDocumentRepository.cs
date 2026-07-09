using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces;

public interface ISourceDocumentRepository
{
    Task AddAsync(SourceDocument document);

    Task<SourceDocument?> GetByIdAsync(Guid id);

    /// <summary>List lịch sử upload (mới nhất trước) + tổng, phân trang.</summary>
    Task<(List<SourceDocument> Items, int Total)> GetHistoryAsync(int pageNumber, int pageSize);

    /// <summary>1 document + các câu (Include nav để có tên chương/loại). Null nếu không thấy.</summary>
    Task<SourceDocument?> GetWithQuestionsAsync(Guid id);

    void Update(SourceDocument document);

    /// <summary>Thêm nhiều câu hỏi cùng lúc (kết quả extract từ PDF).</summary>
    Task AddQuestionsAsync(IEnumerable<QuestionBank> questions);
}
