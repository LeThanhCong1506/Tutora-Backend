using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.RequestModel.Assessment;
using MV.DomainLayer.DTO.ResponseModel.Assessment;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Services;

public class AssessmentService : IAssessmentService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<AssessmentService> _logger;

    public AssessmentService(IUnitOfWork unitOfWork, ILogger<AssessmentService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    // Đề
    public async Task<AssessmentResponse> CreateAsync(
        CreateAssessmentRequest request, string? createdBy, CancellationToken ct = default)
    {
        var entity = new Assessment
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Description = request.Description,
            SubjectId = request.SubjectId,
            GradeLevelId = request.GradeLevelId,
            QuestionCount = request.QuestionCount,
            DurationMinutes = request.DurationMinutes,
            ShuffleQuestions = request.ShuffleQuestions,
            ShuffleOptions = request.ShuffleOptions,
            ShowResult = request.ShowResult,
            // Đề mới rỗng câu -> không cho phát hành ngay.
            Status = AssessmentStatus.IsValid(request.Status) && request.Status != AssessmentStatus.Published
                ? request.Status!
                : AssessmentStatus.Draft,
            CreatedBy = createdBy,
        };

        await _unitOfWork.AssessmentRepository.AddAsync(entity);
        await _unitOfWork.SaveChangesAsync();

        // Đọc lại để có nav Subject/Gradelevel cho response.
        var saved = await _unitOfWork.AssessmentRepository.GetByIdAsync(entity.Id);
        return ToResponse(saved ?? entity, 0, 0);
    }

    public async Task<PagedList<AssessmentResponse>> GetPagedAsync(
        int pageNumber, int pageSize,
        int? subjectId, int? gradeLevelId, string? status, string? search,
        string? sortBy, string? sortDir,
        CancellationToken ct = default)
    {
        var paged = await _unitOfWork.AssessmentRepository.GetPagedAsync(
            pageNumber, pageSize, subjectId, gradeLevelId, status, search, sortBy, sortDir);

        // 1 query cho cả trang, tránh N+1.
        var stats = await _unitOfWork.AssessmentRepository.GetQuestionStatsAsync(
            paged.Select(a => a.Id).ToList());

        var items = paged.Select(a =>
        {
            var (count, points) = stats.TryGetValue(a.Id, out var s) ? s : (0, 0m);
            return ToResponse(a, count, points);
        }).ToList();

        return new PagedList<AssessmentResponse>(items, paged.TotalCount, paged.CurrentPage, paged.PageSize);
    }

    public async Task<AssessmentDetailResponse?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _unitOfWork.AssessmentRepository.GetByIdWithQuestionsAsync(id);
        if (entity == null) return null;

        var count = entity.Questions.Count;
        var points = entity.Questions.Sum(q => q.Points);
        var basic = ToResponse(entity, count, points);

        return new AssessmentDetailResponse
        {
            Id = basic.Id,
            Title = basic.Title,
            Description = basic.Description,
            SubjectId = basic.SubjectId,
            SubjectName = basic.SubjectName,
            GradeLevelId = basic.GradeLevelId,
            GradeName = basic.GradeName,
            QuestionCount = basic.QuestionCount,
            DurationMinutes = basic.DurationMinutes,
            ShuffleQuestions = basic.ShuffleQuestions,
            ShuffleOptions = basic.ShuffleOptions,
            ShowResult = basic.ShowResult,
            Status = basic.Status,
            AssignedQuestionCount = basic.AssignedQuestionCount,
            TotalPoints = basic.TotalPoints,
            IsReady = basic.IsReady,
            CreatedBy = basic.CreatedBy,
            CreatedAt = basic.CreatedAt,
            UpdatedAt = basic.UpdatedAt,
            Questions = entity.Questions.Select(ToQuestionResponse).ToList(),
        };
    }

    public async Task<AssessmentResponse?> UpdateAsync(
        Guid id, UpdateAssessmentRequest request, CancellationToken ct = default)
    {
        var entity = await _unitOfWork.AssessmentRepository.GetByIdAsync(id);
        if (entity == null) return null;

        entity.Title = request.Title.Trim();
        entity.Description = request.Description;
        entity.SubjectId = request.SubjectId;
        entity.GradeLevelId = request.GradeLevelId;
        entity.QuestionCount = request.QuestionCount;
        entity.DurationMinutes = request.DurationMinutes;
        entity.ShuffleQuestions = request.ShuffleQuestions;
        entity.ShuffleOptions = request.ShuffleOptions;
        entity.ShowResult = request.ShowResult;
        // Phát hành đi qua endpoint riêng (kiểm đủ câu); đây chỉ nhận draft/archived.
        if (AssessmentStatus.IsValid(request.Status) && request.Status != AssessmentStatus.Published)
            entity.Status = request.Status!;

        _unitOfWork.AssessmentRepository.Update(entity);
        await _unitOfWork.SaveChangesAsync();

        var stats = await _unitOfWork.AssessmentRepository.GetQuestionStatsAsync(new[] { id });
        var (count, points) = stats.TryGetValue(id, out var s) ? s : (0, 0m);
        return ToResponse(entity, count, points);
    }

    public async Task<(AssessmentResponse? Result, string? Error)> UpdateStatusAsync(
        Guid id, string status, CancellationToken ct = default)
    {
        if (!AssessmentStatus.IsValid(status))
            return (null, "Trạng thái không hợp lệ.");

        var entity = await _unitOfWork.AssessmentRepository.GetByIdAsync(id);
        if (entity == null) return (null, null);   // null cả 2 = không tìm thấy

        var stats = await _unitOfWork.AssessmentRepository.GetQuestionStatsAsync(new[] { id });
        var (count, points) = stats.TryGetValue(id, out var s) ? s : (0, 0m);

        // Chặn phát hành đề thiếu câu.
        if (status == AssessmentStatus.Published)
        {
            if (count == 0)
                return (null, "Đề chưa có câu hỏi nào, không thể phát hành.");
            if (entity.QuestionCount.HasValue && count < entity.QuestionCount.Value)
                return (null, $"Đề cần ít nhất {entity.QuestionCount.Value} câu hỏi nhưng hiện chỉ có {count} câu.");
        }

        entity.Status = status;
        _unitOfWork.AssessmentRepository.Update(entity);
        await _unitOfWork.SaveChangesAsync();

        return (ToResponse(entity, count, points), null);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _unitOfWork.AssessmentRepository.GetByIdAsync(id);
        if (entity == null) return false;

        _unitOfWork.AssessmentRepository.Remove(entity);   // cascade xoá câu
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    // Câu hỏi trong đề
    public async Task<(AssessmentQuestionResponse? Result, string? Error)> AddQuestionAsync(
        Guid assessmentId, CreateAssessmentQuestionRequest request, CancellationToken ct = default)
    {
        var assessment = await _unitOfWork.AssessmentRepository.GetByIdAsync(assessmentId);
        if (assessment == null) return (null, null);   // null cả 2 = không tìm thấy đề

        var (format, formatError) = await ResolveFormatAsync(request.QuestionTypeId);
        if (formatError != null) return (null, formatError);

        var error = AssessmentQuestionValidator.Validate(request, format!);
        if (error != null) return (null, error);

        // Bỏ trống DisplayOrder = thêm vào cuối đề.
        var order = request.DisplayOrder
            ?? await _unitOfWork.AssessmentRepository.GetMaxDisplayOrderAsync(assessmentId) + 1;

        var entity = new AssessmentQuestion
        {
            Id = Guid.NewGuid(),
            AssessmentId = assessmentId,
            DisplayOrder = order,
            Points = request.Points,
            QuestionFormat = format!,
            ChapterId = request.ChapterId,
            QuestionTypeId = request.QuestionTypeId,
            Difficulty = request.Difficulty,
            Content = request.Content,
            AnswerOptions = ToAnswerOptions(request, format!),
            CorrectAnswer = NormalizeCorrectAnswer(request, format!),
            AcceptedAnswers = ToAcceptedAnswers(request, format!),
            Explanation = request.Explanation,
            ImageUrls = request.ImageUrls ?? new(),
        };

        await _unitOfWork.AssessmentRepository.AddQuestionAsync(entity);
        await _unitOfWork.SaveChangesAsync();

        var saved = await _unitOfWork.AssessmentRepository.GetQuestionByIdAsync(entity.Id);
        return (ToQuestionResponse(saved ?? entity), null);
    }

    public async Task<(AssessmentQuestionResponse? Result, string? Error)> UpdateQuestionAsync(
        Guid assessmentId, Guid questionId, UpdateAssessmentQuestionRequest request, CancellationToken ct = default)
    {
        var entity = await _unitOfWork.AssessmentRepository.GetQuestionByIdAsync(questionId);
        // Chặn sửa chéo đề qua URL.
        if (entity == null || entity.AssessmentId != assessmentId) return (null, null);

        var (format, formatError) = await ResolveFormatAsync(request.QuestionTypeId);
        if (formatError != null) return (null, formatError);

        var error = AssessmentQuestionValidator.Validate(request, format!);
        if (error != null) return (null, error);

        entity.Points = request.Points;
        entity.QuestionFormat = format!;
        entity.ChapterId = request.ChapterId;
        entity.QuestionTypeId = request.QuestionTypeId;
        entity.Difficulty = request.Difficulty;
        entity.Content = request.Content;
        entity.AnswerOptions = ToAnswerOptions(request, format!);
        entity.CorrectAnswer = NormalizeCorrectAnswer(request, format!);
        entity.AcceptedAnswers = ToAcceptedAnswers(request, format!);
        entity.Explanation = request.Explanation;
        entity.ImageUrls = request.ImageUrls ?? new();
        if (request.DisplayOrder.HasValue)
            entity.DisplayOrder = request.DisplayOrder.Value;

        _unitOfWork.AssessmentRepository.UpdateQuestion(entity);
        await _unitOfWork.SaveChangesAsync();

        var saved = await _unitOfWork.AssessmentRepository.GetQuestionByIdAsync(questionId);
        return (ToQuestionResponse(saved ?? entity), null);
    }

    public async Task<bool> DeleteQuestionAsync(Guid assessmentId, Guid questionId, CancellationToken ct = default)
    {
        var entity = await _unitOfWork.AssessmentRepository.GetQuestionByIdAsync(questionId);
        if (entity == null || entity.AssessmentId != assessmentId) return false;

        _unitOfWork.AssessmentRepository.RemoveQuestion(entity);
        await _unitOfWork.SaveChangesAsync();

        // Dồn lại thứ tự: 1,2,4 -> 1,2,3.
        var remaining = await _unitOfWork.AssessmentRepository.GetQuestionsByAssessmentAsync(assessmentId);
        ApplyOrder(remaining);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<(bool Ok, string? Error)> ReorderQuestionsAsync(
        Guid assessmentId, IReadOnlyList<Guid> questionIds, CancellationToken ct = default)
    {
        var questions = await _unitOfWork.AssessmentRepository.GetQuestionsByAssessmentAsync(assessmentId);
        if (questions.Count == 0) return (false, null);   // đề không tồn tại / chưa có câu

        // Phải liệt kê ĐỦ câu của đề, thiếu 1 câu thì thứ tự thành mơ hồ.
        var ids = questionIds.Distinct().ToList();
        if (ids.Count != questions.Count || ids.Any(id => questions.All(q => q.Id != id)))
            return (false, "Danh sách câu hỏi không khớp với các câu hiện có trong đề.");

        var byId = questions.ToDictionary(q => q.Id);
        for (int i = 0; i < ids.Count; i++)
        {
            var q = byId[ids[i]];
            q.DisplayOrder = i + 1;
            _unitOfWork.AssessmentRepository.UpdateQuestion(q);
        }
        await _unitOfWork.SaveChangesAsync();

        return (true, null);
    }

    // Helpers
    /// <summary>Gán lại display_order 1..n.</summary>
    private void ApplyOrder(List<AssessmentQuestion> questions)
    {
        for (int i = 0; i < questions.Count; i++)
        {
            if (questions[i].DisplayOrder == i + 1) continue;
            questions[i].DisplayOrder = i + 1;
            _unitOfWork.AssessmentRepository.UpdateQuestion(questions[i]);
        }
    }

    /// <summary>Cách chấm suy từ slug. Mọi loại đều dùng được; loại lạ -> tự luận.</summary>
    private async Task<(string? Format, string? Error)> ResolveFormatAsync(int questionTypeId)
    {
        var slug = await _unitOfWork.AssessmentRepository.GetQuestionTypeSlugAsync(questionTypeId);
        return slug == null
            ? (null, "Loại câu hỏi không tồn tại hoặc đã ngừng dùng.")
            : (QuestionTypeFormatMapper.Resolve(slug), null);
    }

    /// <summary>Trả lời ngắn không có phương án; loại khác bỏ phương án rỗng.</summary>
    private static List<AnswerOption>? ToAnswerOptions(CreateAssessmentQuestionRequest r, string format)
    {
        if (!AssessmentQuestionFormat.RequiresOptions(format)) return null;

        return r.AnswerOptions?
            .Where(o => !string.IsNullOrWhiteSpace(o.Key) && !string.IsNullOrWhiteSpace(o.Text))
            .Select(o => new AnswerOption { Key = o.Key.Trim(), Text = o.Text })
            .ToList();
    }

    /// <summary>Chỉ loại trả lời ngắn mới giữ AcceptedAnswers.</summary>
    private static List<string>? ToAcceptedAnswers(CreateAssessmentQuestionRequest r, string format)
    {
        if (format != AssessmentQuestionFormat.ShortAnswer) return null;

        var list = r.AcceptedAnswers?
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .Distinct()
            .ToList();
        return list is { Count: > 0 } ? list : null;
    }

    /// <summary>CSV key sắp thứ tự cố định để chấm không phụ thuộc thứ tự admin nhập.</summary>
    private static string NormalizeCorrectAnswer(CreateAssessmentQuestionRequest r, string format)
    {
        var raw = r.CorrectAnswer.Trim();
        if (!AssessmentQuestionFormat.RequiresOptions(format)) return raw;

        var keys = AssessmentQuestionValidator.SplitKeys(raw)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase);
        return string.Join(",", keys);
    }

    private static AssessmentResponse ToResponse(Assessment a, int assignedCount, decimal totalPoints) => new()
    {
        Id = a.Id,
        Title = a.Title,
        Description = a.Description,
        SubjectId = a.SubjectId,
        SubjectName = a.Subject?.Subjectname,
        GradeLevelId = a.GradeLevelId,
        GradeName = a.Gradelevel?.Gradename,
        QuestionCount = a.QuestionCount,
        DurationMinutes = a.DurationMinutes,
        ShuffleQuestions = a.ShuffleQuestions,
        ShuffleOptions = a.ShuffleOptions,
        ShowResult = a.ShowResult,
        Status = a.Status,
        AssignedQuestionCount = assignedCount,
        TotalPoints = totalPoints,
        IsReady = assignedCount > 0 && (!a.QuestionCount.HasValue || assignedCount >= a.QuestionCount.Value),
        CreatedBy = a.CreatedBy,
        CreatedAt = a.CreatedAt,
        UpdatedAt = a.UpdatedAt,
    };

    // nav (ChapterNav/QuestionType) cần Include để có Name — null-safe nếu chưa Include.
    private static AssessmentQuestionResponse ToQuestionResponse(AssessmentQuestion q) => new()
    {
        Id = q.Id,
        AssessmentId = q.AssessmentId,
        DisplayOrder = q.DisplayOrder,
        Points = q.Points,
        QuestionFormat = q.QuestionFormat,
        ChapterId = q.ChapterId,
        ChapterName = q.ChapterNav?.Name,
        QuestionTypeId = q.QuestionTypeId,
        QuestionTypeName = q.QuestionType?.Name,
        Difficulty = q.Difficulty,
        Content = q.Content,
        AnswerOptions = q.AnswerOptions?
            .Select(o => new AssessmentAnswerOptionResponse { Key = o.Key, Text = o.Text }).ToList(),
        CorrectAnswer = q.CorrectAnswer,
        AcceptedAnswers = q.AcceptedAnswers,
        Explanation = q.Explanation,
        ImageUrls = q.ImageUrls ?? new(),
        CreatedAt = q.CreatedAt,
        UpdatedAt = q.UpdatedAt,
    };
}
