using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;

namespace MV.InfrastructureLayer.Repositories;

public class SourceDocumentRepository : ISourceDocumentRepository
{
    private readonly AgoraDbContext _context;

    public SourceDocumentRepository(AgoraDbContext context)
        => _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task AddAsync(SourceDocument document)
        => await _context.SourceDocuments.AddAsync(document);

    public Task<SourceDocument?> GetByIdAsync(Guid id)
        => _context.SourceDocuments.FirstOrDefaultAsync(d => d.Id == id);

    public async Task<(List<SourceDocument> Items, int Total)> GetHistoryAsync(int pageNumber, int pageSize)
    {
        var query = _context.SourceDocuments.AsNoTracking().OrderByDescending(d => d.CreatedAt);
        var total = await query.CountAsync();
        var items = await query.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();
        return (items, total);
    }

    public Task<SourceDocument?> GetWithQuestionsAsync(Guid id)
        => _context.SourceDocuments
            .AsNoTracking()
            .Include(d => d.Questions).ThenInclude(q => q.ChapterNav)
            .Include(d => d.Questions).ThenInclude(q => q.QuestionType)
            .Include(d => d.Questions).ThenInclude(q => q.Subject)
            .Include(d => d.Questions).ThenInclude(q => q.Gradelevel)
            .FirstOrDefaultAsync(d => d.Id == id);

    public void Update(SourceDocument document)
        => _context.SourceDocuments.Update(document);

    public async Task AddQuestionsAsync(IEnumerable<QuestionBank> questions)
        => await _context.QuestionBanks.AddRangeAsync(questions);
}
