using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.RequestModel.Assessment;
using MV.DomainLayer.DTO.ResponseModel.Assessment;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Services;

public class AssessmentAttemptService : IAssessmentAttemptService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<AssessmentAttemptService> _logger;

    public AssessmentAttemptService(IUnitOfWork unitOfWork, ILogger<AssessmentAttemptService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    // Bắt đầu / tiếp tục làm bài 

    public async Task<(AttemptInProgressResponse? Result, string? Error)> StartAsync(
        Guid assessmentId, string userId, CancellationToken ct = default)
    {
        var assessment = await _unitOfWork.AssessmentRepository.GetByIdWithQuestionsAsync(assessmentId);
        if (assessment == null) return (null, null);   // null cả 2 = không tìm thấy

        if (assessment.Status != AssessmentStatus.Published)
            return (null, "Đề này chưa được phát hành.");
        if (assessment.Questions.Count == 0)
            return (null, "Đề chưa có câu hỏi.");

        // Còn bài dở -> tiếp tục bài đó (unique index cũng chặn ở DB).
        var existing = await _unitOfWork.AssessmentRepository.GetInProgressAttemptAsync(assessmentId, userId);
        if (existing != null)
        {
            // Quá giờ -> đóng bài cũ, cho làm bài mới.
            if (existing.ExpiresAt.HasValue && existing.ExpiresAt.Value <= DateTime.UtcNow)
            {
                existing.Status = AssessmentAttemptStatus.Abandoned;
                _unitOfWork.AssessmentRepository.UpdateAttempt(existing);
                await _unitOfWork.SaveChangesAsync();
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

        await _unitOfWork.AssessmentRepository.AddAttemptAsync(attempt);
        await _unitOfWork.SaveChangesAsync();

        return (ToInProgressResponse(assessment, attempt), null);
    }

    // Nộp bài + chấm 

    public async Task<(AttemptResultResponse? Result, string? Error)> SubmitAsync(
        Guid attemptId, string userId, SubmitAttemptRequest request, CancellationToken ct = default)
    {
        var attempt = await _unitOfWork.AssessmentRepository.GetAttemptWithAnswersAsync(attemptId);
        // Chặn nộp bài của học sinh khác qua URL.
        if (attempt == null || attempt.UserId != userId) return (null, null);

        if (attempt.Status == AssessmentAttemptStatus.Submitted)
            return (null, "Bài này đã nộp rồi.");
        if (attempt.Status == AssessmentAttemptStatus.Abandoned)
            return (null, "Bài này đã bị đóng do quá thời gian làm bài.");

        var assessment = await _unitOfWork.AssessmentRepository.GetByIdWithQuestionsAsync(attempt.AssessmentId);
        if (assessment == null) return (null, "Không tìm thấy đề của bài làm này.");

        // Quá giờ vẫn CHẤM, không huỷ trắng bài.
        var now = DateTime.UtcNow;
        var overdue = attempt.ExpiresAt.HasValue && attempt.ExpiresAt.Value < now;

        // Xoá trả lời cũ rồi ghi lại theo payload nộp.
        if (attempt.Answers.Count > 0)
        {
            _unitOfWork.AssessmentRepository.RemoveAttemptAnswers(attempt.Answers);
            await _unitOfWork.SaveChangesAsync();
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

            await _unitOfWork.AssessmentRepository.AddAttemptAnswerAsync(new AssessmentAttemptAnswer
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

        _unitOfWork.AssessmentRepository.UpdateAttempt(attempt);
        await _unitOfWork.SaveChangesAsync();

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
        var attempt = await _unitOfWork.AssessmentRepository.GetAttemptWithAnswersAsync(attemptId);
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
        var paged = await _unitOfWork.AssessmentRepository.GetAttemptsByUserAsync(
            userId, pageNumber, pageSize, subjectId);

        var items = paged.Select(ToResultResponse).ToList();
        return new PagedList<AttemptResultResponse>(items, paged.TotalCount, paged.CurrentPage, paged.PageSize);
    }

    // Dữ kiện cho AI 

    public async Task<AttemptAnalysisInputResponse?> GetAnalysisInputAsync(
        Guid attemptId, CancellationToken ct = default)
    {
        var attempt = await _unitOfWork.AssessmentRepository.GetAttemptWithAnswersAsync(attemptId);
        if (attempt?.Assessment == null) return null;
        // Chưa nộp thì chưa có gì phân tích.
        if (attempt.Status != AssessmentAttemptStatus.Submitted) return null;

        var attemptCount = await _unitOfWork.AssessmentRepository.CountSubmittedAttemptsAsync(
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
        var attempt = await _unitOfWork.AssessmentRepository.GetAttemptAsync(attemptId);
        if (attempt == null || attempt.Status != AssessmentAttemptStatus.Submitted) return false;

        var assessment = await _unitOfWork.AssessmentRepository.GetByIdAsync(attempt.AssessmentId);
        if (assessment == null) return false;

        attempt.AnalysisStatus = AssessmentAnalysisStatus.Done;
        attempt.AnalysisSummary = request.Summary;
        attempt.AnalysisResult = request.AnalysisResult;
        attempt.AnalysisError = null;
        attempt.AnalyzedAt = DateTime.UtcNow;
        _unitOfWork.AssessmentRepository.UpdateAttempt(attempt);

        // Ghi đè profile của (học sinh, môn).
        var attemptCount = await _unitOfWork.AssessmentRepository.CountSubmittedAttemptsAsync(
            attempt.UserId, assessment.SubjectId);

        var profile = await _unitOfWork.AssessmentRepository.GetProficiencyProfileAsync(
            attempt.UserId, assessment.SubjectId);

        // Level lạ -> bỏ qua, đừng để DB reject cả giao dịch.
        var level = ProficiencyLevel.IsValid(request.Level) ? request.Level : null;
        if (request.Level != null && level == null)
            _logger.LogWarning("AI trả level không hợp lệ '{Level}' cho bài {AttemptId} — bỏ qua.",
                request.Level, attemptId);

        if (profile == null)
        {
            await _unitOfWork.AssessmentRepository.AddProficiencyProfileAsync(new StudentProficiencyProfile
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
            _unitOfWork.AssessmentRepository.UpdateProficiencyProfile(profile);
        }

        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public async Task<bool> MarkAnalysisFailedAsync(Guid attemptId, string error, CancellationToken ct = default)
    {
        var attempt = await _unitOfWork.AssessmentRepository.GetAttemptAsync(attemptId);
        if (attempt == null) return false;

        // Bài không mất điểm, chỉ phân tích bị đánh dấu lỗi.
        attempt.AnalysisStatus = AssessmentAnalysisStatus.Failed;
        attempt.AnalysisError = error;
        _unitOfWork.AssessmentRepository.UpdateAttempt(attempt);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    // Profile trình độ 

    public async Task<List<ProficiencyProfileResponse>> GetProficiencyAsync(
        string userId, int? subjectId, CancellationToken ct = default)
    {
        if (subjectId.HasValue)
        {
            var one = await _unitOfWork.AssessmentRepository.GetProficiencyProfileAsync(userId, subjectId.Value);
            return one == null ? new() : new() { ToProfileResponse(one) };
        }

        var all = await _unitOfWork.AssessmentRepository.GetProficiencyProfilesAsync(userId);
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
