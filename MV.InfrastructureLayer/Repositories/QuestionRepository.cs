using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;

namespace MV.InfrastructureLayer.Repositories;

public class QuestionRepository : IQuestionRepository
{
    private readonly AgoraDbContext _context;

    public QuestionRepository(AgoraDbContext context)
        => _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task AddAsync(QuestionBank question)
        => await _context.QuestionBanks.AddAsync(question);

    // Include nav để ToResponse có Name (chương/loại/môn/lớp) hiển thị cho UI.
    private IQueryable<QuestionBank> WithNav(IQueryable<QuestionBank> q) => q
        .Include(x => x.Subject)
        .Include(x => x.Gradelevel)
        .Include(x => x.ChapterNav)
        .Include(x => x.QuestionType);

    public Task<QuestionBank?> GetByIdAsync(Guid id)
        => WithNav(_context.QuestionBanks).FirstOrDefaultAsync(q => q.Id == id);

    public Task<PagedList<QuestionBank>> GetPagedAsync(
        int pageNumber,
        int pageSize,
        int? subjectId = null,
        int? gradeLevelId = null,
        int? chapterId = null,
        string? reviewStatus = null,
        string? search = null)
    {
        var query = WithNav(_context.QuestionBanks.AsNoTracking());

        if (subjectId.HasValue)
            query = query.Where(q => q.SubjectId == subjectId.Value);
        if (gradeLevelId.HasValue)
            query = query.Where(q => q.GradeLevelId == gradeLevelId.Value);
        if (chapterId.HasValue)
            query = query.Where(q => q.ChapterId == chapterId.Value);
        if (!string.IsNullOrWhiteSpace(reviewStatus))
            query = query.Where(q => q.ReviewStatus == reviewStatus);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(q => EF.Functions.ILike(q.Content, $"%{search}%"));

        query = query.OrderByDescending(q => q.CreatedAt);

        // Materialize thu cong (khong dung ToPagedList sync) de await dung EF Core async.
        var count = query.Count();
        var items = query.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult(new PagedList<QuestionBank>(items, count, pageNumber, pageSize));
    }

    public void Update(QuestionBank question)
        => _context.QuestionBanks.Update(question);

    public void Remove(QuestionBank question)
        => _context.QuestionBanks.Remove(question);
}
