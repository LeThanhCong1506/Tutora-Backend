using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;

namespace MV.InfrastructureLayer.Repositories;

public class AssessmentRepository : IAssessmentRepository
{
    private readonly AgoraDbContext _context;

    public AssessmentRepository(AgoraDbContext context)
        => _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task AddAsync(Assessment assessment)
        => await _context.Assessments.AddAsync(assessment);

    // Include môn/lớp để response có Name.
    private IQueryable<Assessment> WithNav(IQueryable<Assessment> q) => q
        .Include(x => x.Subject)
        .Include(x => x.Gradelevel);

    public Task<Assessment?> GetByIdAsync(Guid id)
        => WithNav(_context.Assessments).FirstOrDefaultAsync(a => a.Id == id);

    public async Task<Assessment?> GetByIdWithQuestionsAsync(Guid id)
    {
        var assessment = await WithNav(_context.Assessments)
            .Include(a => a.Questions).ThenInclude(q => q.ChapterNav)
            .Include(a => a.Questions).ThenInclude(q => q.QuestionType)
            .FirstOrDefaultAsync(a => a.Id == id);

        // Sort ở client: EF không cho OrderBy trong ThenInclude, list câu 1 đề nhỏ.
        if (assessment != null)
            assessment.Questions = assessment.Questions
                .OrderBy(q => q.DisplayOrder).ThenBy(q => q.CreatedAt).ToList();

        return assessment;
    }

    public async Task<PagedList<Assessment>> GetPagedAsync(
        int pageNumber,
        int pageSize,
        int? subjectId = null,
        int? gradeLevelId = null,
        string? status = null,
        string? search = null,
        string? sortBy = null,
        string? sortDir = null)
    {
        var query = WithNav(_context.Assessments.AsNoTracking());

        if (subjectId.HasValue)
            query = query.Where(a => a.SubjectId == subjectId.Value);
        if (gradeLevelId.HasValue)
            query = query.Where(a => a.GradeLevelId == gradeLevelId.Value);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(a => a.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(a => EF.Functions.ILike(a.Title, $"%{search}%"));

        // Whitelist cột sort.
        bool asc = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
        query = (sortBy?.ToLowerInvariant()) switch
        {
            "title"      => asc ? query.OrderBy(a => a.Title)        : query.OrderByDescending(a => a.Title),
            "gradelevel" => asc ? query.OrderBy(a => a.GradeLevelId) : query.OrderByDescending(a => a.GradeLevelId),
            "createdat"  => asc ? query.OrderBy(a => a.CreatedAt)    : query.OrderByDescending(a => a.CreatedAt),
            _            => asc ? query.OrderBy(a => a.UpdatedAt)    : query.OrderByDescending(a => a.UpdatedAt),
        };

        var count = await query.CountAsync();
        var items = await query.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PagedList<Assessment>(items, count, pageNumber, pageSize);
    }

    public void Update(Assessment assessment)
        => _context.Assessments.Update(assessment);

    public void Remove(Assessment assessment)
        => _context.Assessments.Remove(assessment);   // cascade xoá câu

    public async Task<Dictionary<Guid, (int Count, decimal TotalPoints)>> GetQuestionStatsAsync(
        IReadOnlyList<Guid> assessmentIds)
    {
        if (assessmentIds.Count == 0) return new();

        var rows = await _context.AssessmentQuestions.AsNoTracking()
            .Where(q => assessmentIds.Contains(q.AssessmentId))
            .GroupBy(q => q.AssessmentId)
            .Select(g => new { AssessmentId = g.Key, Count = g.Count(), TotalPoints = g.Sum(x => x.Points) })
            .ToListAsync();

        return rows.ToDictionary(r => r.AssessmentId, r => (r.Count, r.TotalPoints));
    }

    // Câu hỏi trong đề
    public async Task AddQuestionAsync(AssessmentQuestion question)
        => await _context.AssessmentQuestions.AddAsync(question);

    public Task<AssessmentQuestion?> GetQuestionByIdAsync(Guid questionId)
        => _context.AssessmentQuestions
            .Include(q => q.ChapterNav)
            .Include(q => q.QuestionType)
            .FirstOrDefaultAsync(q => q.Id == questionId);

    public Task<List<AssessmentQuestion>> GetQuestionsByAssessmentAsync(Guid assessmentId)
        => _context.AssessmentQuestions
            .Where(q => q.AssessmentId == assessmentId)
            .OrderBy(q => q.DisplayOrder).ThenBy(q => q.CreatedAt)
            .ToListAsync();

    public async Task<int> GetMaxDisplayOrderAsync(Guid assessmentId)
        => await _context.AssessmentQuestions.AsNoTracking()
            .Where(q => q.AssessmentId == assessmentId)
            .Select(q => (int?)q.DisplayOrder)
            .MaxAsync() ?? 0;

    public void UpdateQuestion(AssessmentQuestion question)
        => _context.AssessmentQuestions.Update(question);

    public void RemoveQuestion(AssessmentQuestion question)
        => _context.AssessmentQuestions.Remove(question);

    public Task<string?> GetQuestionTypeSlugAsync(int questionTypeId)
        => _context.QuestionTypes.AsNoTracking()
            .Where(t => t.Id == questionTypeId && t.IsActive)
            .Select(t => t.Slug)
            .FirstOrDefaultAsync();

    // Học sinh làm bài

    public async Task AddAttemptAsync(AssessmentAttempt attempt)
        => await _context.AssessmentAttempts.AddAsync(attempt);

    public Task<AssessmentAttempt?> GetInProgressAttemptAsync(Guid assessmentId, string userId)
        => _context.AssessmentAttempts
            .Include(a => a.Assessment)
            .Include(a => a.Answers)
            .FirstOrDefaultAsync(a =>
                a.AssessmentId == assessmentId &&
                a.UserId == userId &&
                a.Status == "in_progress");

    public async Task<AssessmentAttempt?> GetAttemptWithAnswersAsync(Guid attemptId)
    {
        var attempt = await _context.AssessmentAttempts
            .Include(a => a.Assessment).ThenInclude(x => x!.Subject)
            .Include(a => a.Assessment).ThenInclude(x => x!.Gradelevel)
            .Include(a => a.Answers).ThenInclude(x => x.Question).ThenInclude(q => q!.ChapterNav)
            .FirstOrDefaultAsync(a => a.Id == attemptId);

        // Sort sẵn để FE không phải tự sort.
        if (attempt != null)
            attempt.Answers = attempt.Answers
                .OrderBy(x => x.Question?.DisplayOrder ?? 0).ToList();

        return attempt;
    }

    public Task<AssessmentAttempt?> GetAttemptAsync(Guid attemptId)
        => _context.AssessmentAttempts.FirstOrDefaultAsync(a => a.Id == attemptId);

    public async Task<PagedList<AssessmentAttempt>> GetAttemptsByUserAsync(
        string userId, int pageNumber, int pageSize, int? subjectId = null)
    {
        var query = _context.AssessmentAttempts.AsNoTracking()
            .Include(a => a.Assessment).ThenInclude(x => x!.Subject)
            .Include(a => a.Assessment).ThenInclude(x => x!.Gradelevel)
            .Where(a => a.UserId == userId);

        if (subjectId.HasValue)
            query = query.Where(a => a.Assessment!.SubjectId == subjectId.Value);

        query = query.OrderByDescending(a => a.CreatedAt);

        var count = await query.CountAsync();
        var items = await query.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PagedList<AssessmentAttempt>(items, count, pageNumber, pageSize);
    }

    public Task<int> CountSubmittedAttemptsAsync(string userId, int subjectId)
        => _context.AssessmentAttempts.AsNoTracking()
            .CountAsync(a =>
                a.UserId == userId &&
                a.Status == "submitted" &&
                a.Assessment!.SubjectId == subjectId);

    public async Task AddAttemptAnswerAsync(AssessmentAttemptAnswer answer)
        => await _context.AssessmentAttemptAnswers.AddAsync(answer);

    public void UpdateAttempt(AssessmentAttempt attempt)
        => _context.AssessmentAttempts.Update(attempt);

    public void UpdateAttemptAnswer(AssessmentAttemptAnswer answer)
        => _context.AssessmentAttemptAnswers.Update(answer);

    public void RemoveAttemptAnswers(IEnumerable<AssessmentAttemptAnswer> answers)
        => _context.AssessmentAttemptAnswers.RemoveRange(answers);

    // Profile trình độ

    public Task<StudentProficiencyProfile?> GetProficiencyProfileAsync(string userId, int subjectId)
        => _context.StudentProficiencyProfiles
            .Include(p => p.Subject)
            .Include(p => p.Gradelevel)
            .FirstOrDefaultAsync(p => p.UserId == userId && p.SubjectId == subjectId);

    public Task<List<StudentProficiencyProfile>> GetProficiencyProfilesAsync(string userId)
        => _context.StudentProficiencyProfiles.AsNoTracking()
            .Include(p => p.Subject)
            .Include(p => p.Gradelevel)
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.SubjectId)
            .ToListAsync();

    public async Task AddProficiencyProfileAsync(StudentProficiencyProfile profile)
        => await _context.StudentProficiencyProfiles.AddAsync(profile);

    public void UpdateProficiencyProfile(StudentProficiencyProfile profile)
        => _context.StudentProficiencyProfiles.Update(profile);
}
