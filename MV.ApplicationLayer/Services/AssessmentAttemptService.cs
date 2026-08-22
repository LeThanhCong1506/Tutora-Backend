using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MV.DomainLayer.Settings;
using System.Net.Http.Json;
using System.Text.Json;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.RequestModel.Assessment;
using MV.DomainLayer.DTO.ResponseModel.Assessment;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Services;

public class AssessmentAttemptService : IAssessmentAttemptService
{
    private readonly IAssessmentRepository _assessmentRepository;
    private readonly IAppDbContext _dbContext;
    private readonly ILogger<AssessmentAttemptService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;

    public AssessmentAttemptService(
        IAssessmentRepository assessmentRepository,
        IAppDbContext dbContext,
        ILogger<AssessmentAttemptService> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration config)
    {
        _assessmentRepository = assessmentRepository;
        _dbContext = dbContext;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _config = config;
    }

    // Chọn đề 

    public async Task<List<AvailableAssessmentResponse>> GetAvailableAsync(
        int? subjectId, int? gradeLevelId, CancellationToken ct = default)
    {
        var list = await _assessmentRepository.GetPublishedAsync(subjectId, gradeLevelId);

        return list.Select(a => new AvailableAssessmentResponse
        {
            Id = a.Id,
            Title = a.Title,
            Description = a.Description,
            SubjectId = a.SubjectId,
            SubjectName = a.Subject?.Subjectname,
            GradeLevelId = a.GradeLevelId,
            GradeName = a.Gradelevel?.Gradename,
            QuestionCount = a.QuestionCount ?? a.Questions.Count,
            DurationMinutes = a.DurationMinutes,
        }).ToList();
    }

    public async Task<(AttemptInProgressResponse? Result, string? Error)> StartRandomAsync(
        int subjectId, int? gradeLevelId, string userId, CancellationToken ct = default)
    {
        var candidates = await _assessmentRepository.GetPublishedAsync(subjectId, gradeLevelId);

        // Không có đề đúng lớp -> nới ra cả môn, thà cho làm đề lệch lớp hơn là chặn.
        if (candidates.Count == 0 && gradeLevelId.HasValue)
            candidates = await _assessmentRepository.GetPublishedAsync(subjectId, null);

        if (candidates.Count == 0)
            return (null, "Chưa có đề đánh giá nào cho môn này.");

        var picked = candidates[Random.Shared.Next(candidates.Count)];
        return await StartAsync(picked.Id, userId, ct);
    }

    // Bắt đầu / tiếp tục làm bài 

    public async Task<(AttemptInProgressResponse? Result, string? Error)> StartAsync(
        Guid assessmentId, string userId, CancellationToken ct = default)
    {
        var assessment = await _assessmentRepository.GetByIdWithQuestionsAsync(assessmentId);
        if (assessment == null) return (null, null);   // null cả 2 = không tìm thấy

        if (assessment.Status != AssessmentStatus.Published)
            return (null, "Đề này chưa được phát hành.");
        if (assessment.Questions.Count == 0)
            return (null, "Đề chưa có câu hỏi.");

        // Còn bài dở -> tiếp tục bài đó (unique index cũng chặn ở DB).
        var existing = await _assessmentRepository.GetInProgressAttemptAsync(assessmentId, userId);
        if (existing != null)
        {
            // Quá giờ -> đóng bài cũ, cho làm bài mới.
            if (existing.ExpiresAt.HasValue && existing.ExpiresAt.Value <= DateTime.UtcNow)
            {
                existing.Status = AssessmentAttemptStatus.Abandoned;
                _assessmentRepository.UpdateAttempt(existing);
                await _dbContext.SaveChangesAsync();
            }
            else
            {
                return (ToInProgressResponse(assessment, existing), null);
            }
        }

        var attempt = new AssessmentAttempt
        {
            Id = Guid.NewGuid(),
            AssessmentId = assessmentId,
            UserId = userId,
            Status = AssessmentAttemptStatus.InProgress,
            StartedAt = DateTime.UtcNow,
            // Chốt deadline lúc bắt đầu — sửa đề giữa lúc đang làm không đổi được.
            ExpiresAt = assessment.DurationMinutes.HasValue
                ? DateTime.UtcNow.AddMinutes(assessment.DurationMinutes.Value)
                : null,
            AnalysisStatus = AssessmentAnalysisStatus.Pending,
        };

        await _assessmentRepository.AddAttemptAsync(attempt);
        await _dbContext.SaveChangesAsync();

        return (ToInProgressResponse(assessment, attempt), null);
    }

    // Nộp bài + chấm 

    public async Task<(AttemptResultResponse? Result, string? Error)> SubmitAsync(
        Guid attemptId, string userId, SubmitAttemptRequest request, CancellationToken ct = default)
    {
        var attempt = await _assessmentRepository.GetAttemptWithAnswersAsync(attemptId);
        // Chặn nộp bài của học sinh khác qua URL.
        if (attempt == null || attempt.UserId != userId) return (null, null);

        if (attempt.Status == AssessmentAttemptStatus.Submitted)
            return (null, "Bài này đã nộp rồi.");
        if (attempt.Status == AssessmentAttemptStatus.Abandoned)
            return (null, "Bài này đã bị đóng do quá thời gian làm bài.");

        var assessment = await _assessmentRepository.GetByIdWithQuestionsAsync(attempt.AssessmentId);
        if (assessment == null) return (null, "Không tìm thấy đề của bài làm này.");

        // Quá giờ vẫn CHẤM, không huỷ trắng bài.
        var now = DateTime.UtcNow;
        var overdue = attempt.ExpiresAt.HasValue && attempt.ExpiresAt.Value < now;

        // Xoá trả lời cũ rồi ghi lại theo payload nộp.
        if (attempt.Answers.Count > 0)
        {
            _assessmentRepository.RemoveAttemptAnswers(attempt.Answers);
            await _dbContext.SaveChangesAsync();
        }

        var givenByQuestion = request.Answers
            .GroupBy(a => a.QuestionId)
            .ToDictionary(g => g.Key, g => g.Last());   // gửi trùng -> lấy lần cuối

        int correctCount = 0;
        decimal earned = 0, max = 0;

        foreach (var question in assessment.Questions)
        {
            max += question.Points;

            givenByQuestion.TryGetValue(question.Id, out var given);
            var rawAnswer = given?.GivenAnswer;
            // Chuỗi rỗng = bỏ trống, lưu null để AI phân biệt với trả lời sai.
            var storedAnswer = string.IsNullOrWhiteSpace(rawAnswer) ? null : rawAnswer.Trim();

            var isCorrect = AssessmentGrader.IsCorrect(question, storedAnswer);
            if (isCorrect)
            {
                correctCount++;
                earned += question.Points;
            }

            await _assessmentRepository.AddAttemptAnswerAsync(new AssessmentAttemptAnswer
            {
                Id = Guid.NewGuid(),
                AttemptId = attempt.Id,
                QuestionId = question.Id,
                GivenAnswer = storedAnswer,
                IsCorrect = isCorrect,
                EarnedPoints = isCorrect ? question.Points : 0,
                // Snapshot: sửa đề sau này không làm sai lệch phân tích cũ.
                ChapterId = question.ChapterId,
                ChapterSlug = question.ChapterNav?.Slug,
                Difficulty = question.Difficulty,
                QuestionFormat = question.QuestionFormat,
                TimeSpentSeconds = given?.TimeSpentSeconds,
            });
        }

        attempt.Status = AssessmentAttemptStatus.Submitted;
        attempt.SubmittedAt = now;
        attempt.TotalQuestions = assessment.Questions.Count;
        attempt.CorrectCount = correctCount;
        attempt.EarnedPoints = earned;
        attempt.MaxPoints = max;
        // Số đo thuần, không so ngưỡng nào.
        attempt.ScorePercent = max > 0 ? Math.Round(earned / max * 100, 2) : null;
        attempt.DurationSeconds = (int)Math.Max(0, (now - attempt.StartedAt).TotalSeconds);
        // Chấm xong rồi; AI phân tích sau, không phụ thuộc.
        attempt.AnalysisStatus = AssessmentAnalysisStatus.Pending;

        _assessmentRepository.UpdateAttempt(attempt);
        await _dbContext.SaveChangesAsync();

        if (overdue)
            _logger.LogInformation("Bài làm {AttemptId} nộp sau deadline {ExpiresAt} — vẫn chấm bình thường.",
                attempt.Id, attempt.ExpiresAt);

        var result = await GetResultAsync(attemptId, userId, ct);
        return (result, null);
    }

    // Kết quả 

    public async Task<AttemptResultResponse?> GetResultAsync(
        Guid attemptId, string userId, CancellationToken ct = default)
    {
        var attempt = await _assessmentRepository.GetAttemptWithAnswersAsync(attemptId);
        if (attempt == null || attempt.UserId != userId) return null;

        var response = ToResultResponse(attempt);

        response.Answers = attempt.Answers.Select(a => new AttemptAnswerResultResponse
        {
            QuestionId = a.QuestionId,
            DisplayOrder = a.Question?.DisplayOrder ?? 0,
            Content = a.Question?.Content ?? "",
            QuestionFormat = a.QuestionFormat ?? a.Question?.QuestionFormat ?? "",
            AnswerOptions = a.Question?.AnswerOptions?
                .Select(o => new AssessmentAnswerOptionResponse { Key = o.Key, Text = o.Text }).ToList(),
            GivenAnswer = a.GivenAnswer,
            IsCorrect = a.IsCorrect,
            EarnedPoints = a.EarnedPoints,
            Points = a.Question?.Points ?? 0,
            // Đáp án LUÔN trả — học sinh luôn được xem. showResult chỉ gác phần điểm.
            CorrectAnswer = a.Question?.CorrectAnswer,
            Explanation = a.Question?.Explanation,
            ChapterName = a.Question?.ChapterNav?.Name,
            Difficulty = a.Difficulty,
        }).ToList();

        return response;
    }

    public async Task<PagedList<AttemptResultResponse>> GetHistoryAsync(
        string userId, int pageNumber, int pageSize, int? subjectId, CancellationToken ct = default)
    {
        var paged = await _assessmentRepository.GetAttemptsByUserAsync(
            userId, pageNumber, pageSize, subjectId);

        var items = paged.Select(ToResultResponse).ToList();
        return new PagedList<AttemptResultResponse>(items, paged.TotalCount, paged.CurrentPage, paged.PageSize);
    }

    // Dữ kiện cho AI 

    public async Task<AttemptAnalysisInputResponse?> GetAnalysisInputAsync(
        Guid attemptId, CancellationToken ct = default)
    {
        var attempt = await _assessmentRepository.GetAttemptWithAnswersAsync(attemptId);
        if (attempt?.Assessment == null) return null;
        // Chưa nộp thì chưa có gì phân tích.
        if (attempt.Status != AssessmentAttemptStatus.Submitted) return null;

        var attemptCount = await _assessmentRepository.CountSubmittedAttemptsAsync(
            attempt.UserId, attempt.Assessment.SubjectId);

        var items = attempt.Answers.Select(a => new AnalysisItemResponse
        {
            DisplayOrder = a.Question?.DisplayOrder ?? 0,
            Content = a.Question?.Content ?? "",
            QuestionFormat = a.QuestionFormat ?? "",
            ChapterName = a.Question?.ChapterNav?.Name,
            ChapterSlug = a.ChapterSlug,
            Difficulty = a.Difficulty,
            CorrectAnswer = a.Question?.CorrectAnswer ?? "",
            GivenAnswer = a.GivenAnswer,
            Skipped = a.GivenAnswer == null,
            IsCorrect = a.IsCorrect,
            TimeSpentSeconds = a.TimeSpentSeconds,
        }).ToList();

        return new AttemptAnalysisInputResponse
        {
            AttemptId = attempt.Id,
            UserId = attempt.UserId,
            SubjectId = attempt.Assessment.SubjectId,
            SubjectName = attempt.Assessment.Subject?.Subjectname,
            GradeLevelId = attempt.Assessment.GradeLevelId,
            GradeName = attempt.Assessment.Gradelevel?.Gradename,
            AssessmentTitle = attempt.Assessment.Title,
            TotalQuestions = attempt.TotalQuestions,
            CorrectCount = attempt.CorrectCount,
            EarnedPoints = attempt.EarnedPoints,
            MaxPoints = attempt.MaxPoints,
            ScorePercent = attempt.ScorePercent,
            DurationSeconds = attempt.DurationSeconds,
            AttemptCount = attemptCount,
            Items = items,
            // Tổng hợp sẵn để AI không tự đếm sai.
            ChapterStats = attempt.Answers
                .GroupBy(a => new { a.ChapterId, a.ChapterSlug, Name = a.Question?.ChapterNav?.Name })
                .Select(g => new AnalysisChapterStatResponse
                {
                    ChapterId = g.Key.ChapterId,
                    ChapterName = g.Key.Name,
                    ChapterSlug = g.Key.ChapterSlug,
                    Total = g.Count(),
                    Correct = g.Count(x => x.IsCorrect),
                    Skipped = g.Count(x => x.GivenAnswer == null),
                })
                .OrderBy(s => s.ChapterName)
                .ToList(),
            DifficultyStats = attempt.Answers
                .GroupBy(a => a.Difficulty)
                .Select(g => new AnalysisDifficultyStatResponse
                {
                    Difficulty = g.Key,
                    Total = g.Count(),
                    Correct = g.Count(x => x.IsCorrect),
                    Skipped = g.Count(x => x.GivenAnswer == null),
                })
                .ToList(),
        };
    }

    public async Task<bool> SaveAnalysisAsync(
        Guid attemptId, SaveAnalysisRequest request, CancellationToken ct = default)
    {
        var attempt = await _assessmentRepository.GetAttemptAsync(attemptId);
        if (attempt == null || attempt.Status != AssessmentAttemptStatus.Submitted) return false;

        var assessment = await _assessmentRepository.GetByIdAsync(attempt.AssessmentId);
        if (assessment == null) return false;

        attempt.AnalysisStatus = AssessmentAnalysisStatus.Done;
        attempt.AnalysisSummary = request.Summary;
        attempt.AnalysisResult = request.AnalysisResult;
        attempt.AnalysisError = null;
        attempt.AnalyzedAt = DateTime.UtcNow;
        _assessmentRepository.UpdateAttempt(attempt);

        // Ghi đè profile của (học sinh, môn).
        var attemptCount = await _assessmentRepository.CountSubmittedAttemptsAsync(
            attempt.UserId, assessment.SubjectId);

        var profile = await _assessmentRepository.GetProficiencyProfileAsync(
            attempt.UserId, assessment.SubjectId);

        // Level lạ -> bỏ qua, đừng để DB reject cả giao dịch.
        var level = ProficiencyLevel.IsValid(request.Level) ? request.Level : null;
        if (request.Level != null && level == null)
            _logger.LogWarning("AI trả level không hợp lệ '{Level}' cho bài {AttemptId} — bỏ qua.",
                request.Level, attemptId);

        if (profile == null)
        {
            await _assessmentRepository.AddProficiencyProfileAsync(new StudentProficiencyProfile
            {
                Id = Guid.NewGuid(),
                UserId = attempt.UserId,
                SubjectId = assessment.SubjectId,
                GradeLevelId = assessment.GradeLevelId,
                Level = level,
                Summary = request.Summary,
                Strengths = request.Strengths,
                Weaknesses = request.Weaknesses,
                RecommendedPath = request.RecommendedPath,
                SourceAttemptId = attempt.Id,
                AttemptCount = attemptCount,
            });
        }
        else
        {
            profile.GradeLevelId = assessment.GradeLevelId;
            profile.Level = level ?? profile.Level;   // AI không kết luận -> giữ mức cũ
            profile.Summary = request.Summary;
            profile.Strengths = request.Strengths;
            profile.Weaknesses = request.Weaknesses;
            profile.RecommendedPath = request.RecommendedPath;
            profile.SourceAttemptId = attempt.Id;
            profile.AttemptCount = attemptCount;
            _assessmentRepository.UpdateProficiencyProfile(profile);
        }

        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> MarkAnalysisFailedAsync(Guid attemptId, string error, CancellationToken ct = default)
    {
        var attempt = await _assessmentRepository.GetAttemptAsync(attemptId);
        if (attempt == null) return false;

        // Bài không mất điểm, chỉ phân tích bị đánh dấu lỗi.
        attempt.AnalysisStatus = AssessmentAnalysisStatus.Failed;
        attempt.AnalysisError = error;
        _assessmentRepository.UpdateAttempt(attempt);
        await _dbContext.SaveChangesAsync();
        return true;
    }

    // Gọi tutora-ai phân tích 

    /// <summary>
    /// Lấy dữ kiện thô -> tutora-ai /analyze-assessment -> ghi profile. Gộp 3 chặng ở BE
    /// vì tutora-ai chưa có internal-key để tự gọi ngược vào đây, và để API key của AI
    /// không phải lộ ra FE.
    /// </summary>
    public async Task<(AttemptAnalysisResultResponse? Result, string? Error)> RunAnalysisAsync(
        Guid attemptId, CancellationToken ct = default)
    {
        var input = await GetAnalysisInputAsync(attemptId, ct);
        if (input == null) return (null, null);

        var attempt = await _assessmentRepository.GetAttemptAsync(attemptId);
        if (attempt == null) return (null, null);

        // processing: chặn 2 lời gọi song song cùng phân tích 1 bài.
        attempt.AnalysisStatus = AssessmentAnalysisStatus.Processing;
        _assessmentRepository.UpdateAttempt(attempt);
        await _dbContext.SaveChangesAsync();

        string raw;
        try
        {
            var client = _httpClientFactory.CreateClient(ServiceKeys.HttpClients.TutorAi);
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/analyze-assessment")
            {
                Content = JsonContent.Create(input),
            };
            var apiKey = _config[$"{TutorAiSettings.SectionName}:ApiKey"];
            if (!string.IsNullOrEmpty(apiKey))
                req.Headers.Add("X-API-Key", apiKey);

            using var resp = await client.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            raw = await resp.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gọi tutora-ai phân tích bài {AttemptId} thất bại.", attemptId);
            await MarkAnalysisFailedAsync(attemptId, ex.Message, ct);
            return (null, "AI đang không phản hồi. Bài làm vẫn được giữ nguyên, bạn thử lại sau nhé.");
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var save = doc.RootElement.GetProperty("saveRequest");

            string? Str(string name) =>
                save.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null
                    ? v.GetString()
                    : null;

            await SaveAnalysisAsync(attemptId, new SaveAnalysisRequest
            {
                Summary = Str("summary"),
                Level = Str("level"),
                Strengths = Str("strengths"),
                Weaknesses = Str("weaknesses"),
                RecommendedPath = Str("recommendedPath"),
                AnalysisResult = Str("analysisResult"),
            }, ct);

            return (new AttemptAnalysisResultResponse
            {
                AttemptId = attemptId,
                // Khối đầy đủ (có chapterMastery) — FE render mindmap từ đây.
                Analysis = doc.RootElement.TryGetProperty("analysis", out var a)
                    ? a.GetRawText()
                    : null,
            }, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI trả dữ liệu không đọc được cho bài {AttemptId}.", attemptId);
            await MarkAnalysisFailedAsync(attemptId, ex.Message, ct);
            return (null, "Kết quả phân tích không đọc được. Bạn thử lại sau nhé.");
        }
    }

    // Profile trình độ 

    public async Task<List<ProficiencyProfileResponse>> GetProficiencyAsync(
        string userId, int? subjectId, CancellationToken ct = default)
    {
        if (subjectId.HasValue)
        {
            var one = await _assessmentRepository.GetProficiencyProfileAsync(userId, subjectId.Value);
            return one == null ? new() : new() { ToProfileResponse(one) };
        }

        var all = await _assessmentRepository.GetProficiencyProfilesAsync(userId);
        return all.Select(ToProfileResponse).ToList();
    }

    // Mapping 

    /// <summary>Đề như học sinh thấy: không kèm đáp án. Trộn + cắt theo cấu hình đề.</summary>
    private static AttemptInProgressResponse ToInProgressResponse(Assessment assessment, AssessmentAttempt attempt)
    {
        var answered = attempt.Answers.ToDictionary(a => a.QuestionId, a => a.GivenAnswer);

        IEnumerable<AssessmentQuestion> questions = assessment.Questions;
        if (assessment.ShuffleQuestions)
            // Seed theo attemptId: reload giữa bài vẫn giữ thứ tự đã phát.
            questions = questions.OrderBy(q => StableHash(attempt.Id, q.Id));
        if (assessment.QuestionCount.HasValue)
            questions = questions.Take(assessment.QuestionCount.Value);

        var list = questions.Select((q, i) => new AttemptQuestionResponse
        {
            Id = q.Id,
            DisplayOrder = i + 1,
            Points = q.Points,
            QuestionFormat = q.QuestionFormat,
            Content = q.Content,
            AnswerOptions = OrderOptions(assessment, attempt, q),
            ImageUrls = q.ImageUrls ?? new(),
            GivenAnswer = answered.TryGetValue(q.Id, out var given) ? given : null,
        }).ToList();

        return new AttemptInProgressResponse
        {
            AttemptId = attempt.Id,
            AssessmentId = assessment.Id,
            Title = assessment.Title,
            Description = assessment.Description,
            SubjectName = assessment.Subject?.Subjectname,
            GradeName = assessment.Gradelevel?.Gradename,
            DurationMinutes = assessment.DurationMinutes,
            StartedAt = attempt.StartedAt,
            ExpiresAt = attempt.ExpiresAt,
            Questions = list,
        };
    }

    private static List<AssessmentAnswerOptionResponse>? OrderOptions(
        Assessment assessment, AssessmentAttempt attempt, AssessmentQuestion question)
    {
        if (question.AnswerOptions == null) return null;

        IEnumerable<AnswerOption> options = question.AnswerOptions;
        // Đúng/Sai không trộn: mệnh đề a/b/c/d phụ thuộc thứ tự.
        if (assessment.ShuffleOptions && question.QuestionFormat != AssessmentQuestionFormat.TrueFalse)
            options = options.OrderBy(o => StableHash(attempt.Id, question.Id, o.Key));

        return options.Select(o => new AssessmentAnswerOptionResponse { Key = o.Key, Text = o.Text }).ToList();
    }

    /// <summary>Hash ổn định để trộn tái lập được. Không dùng Random/GetHashCode.</summary>
    private static int StableHash(Guid attemptId, Guid questionId, string? extra = null)
    {
        var seed = $"{attemptId:N}:{questionId:N}:{extra}";
        unchecked
        {
            int hash = 17;
            foreach (var c in seed) hash = hash * 31 + c;
            return hash & int.MaxValue;
        }
    }

    private static AttemptResultResponse ToResultResponse(AssessmentAttempt a) => new()
    {
        AttemptId = a.Id,
        AssessmentId = a.AssessmentId,
        Title = a.Assessment?.Title ?? "",
        SubjectName = a.Assessment?.Subject?.Subjectname,
        GradeName = a.Assessment?.Gradelevel?.Gradename,
        Status = a.Status,
        TotalQuestions = a.TotalQuestions,
        CorrectCount = a.CorrectCount,
        EarnedPoints = a.EarnedPoints,
        MaxPoints = a.MaxPoints,
        ScorePercent = a.ScorePercent,
        DurationSeconds = a.DurationSeconds,
        StartedAt = a.StartedAt,
        SubmittedAt = a.SubmittedAt,
        AnalysisStatus = a.AnalysisStatus,
        AnalysisSummary = a.AnalysisSummary,
        AnalysisResult = a.AnalysisResult,
        AnalyzedAt = a.AnalyzedAt,
        ShowResult = a.Assessment?.ShowResult ?? false,
    };

    private static ProficiencyProfileResponse ToProfileResponse(StudentProficiencyProfile p) => new()
    {
        Id = p.Id,
        UserId = p.UserId,
        SubjectId = p.SubjectId,
        SubjectName = p.Subject?.Subjectname,
        GradeLevelId = p.GradeLevelId,
        GradeName = p.Gradelevel?.Gradename,
        Level = p.Level,
        Summary = p.Summary,
        Strengths = p.Strengths,
        Weaknesses = p.Weaknesses,
        RecommendedPath = p.RecommendedPath,
        SourceAttemptId = p.SourceAttemptId,
        AttemptCount = p.AttemptCount,
        UpdatedAt = p.UpdatedAt,
    };
}
