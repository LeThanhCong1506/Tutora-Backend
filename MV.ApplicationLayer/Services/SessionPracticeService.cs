using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Bài tập nhanh trong buổi học.
/// </summary>
public class SessionPracticeService(
    ISessionPracticeRepository repo,
    IBookingRepository bookingRepository,
    ILearningMaterialRepository materialRepository,
    ITutorAiClient aiClient,
    ILogger<SessionPracticeService> logger) : ISessionPracticeService
{
    public async Task<List<SessionPracticeSetResponse>> GetSetsAsync(int bookingId, string actorUserId)
    {
        var booking = await bookingRepository.FindWithStudentAsync(bookingId)
            ?? throw new BookingNotFoundException();

        var isTutor = booking.Tutorid == actorUserId;
        if (!isTutor && !IsStudentOfBooking(booking, actorUserId) && booking.Parentid != actorUserId)
            throw new PracticeAccessDeniedException();

        var sets = await repo.GetSetsByBookingAsync(bookingId, sentOnly: !isTutor);

        // Học sinh: ghép bài làm của chính em vào từng câu
        Dictionary<Guid, SessionPracticeAnswer> myAnswers = [];
        if (!isTutor)
        {
            var questionIds = sets.SelectMany(s => s.Questions).Select(q => q.Id).ToList();
            if (questionIds.Count > 0)
            {
                var answers = await repo.GetAnswersAsync(actorUserId, questionIds);
                myAnswers = answers.ToDictionary(a => a.QuestionId);
            }
        }

        return sets.Select(s => MapSet(s, isTutor, myAnswers)).ToList();
    }

    public async Task<SessionPracticeSetResponse> GenerateAsync(
        int bookingId, string tutorUserId, GenerateSessionPracticeRequest request)
    {
        var booking = await bookingRepository.FindWithStudentAsync(bookingId)
            ?? throw new BookingNotFoundException();

        if (booking.Tutorid != tutorUserId)
            throw new PracticeAccessDeniedException();

        // Chỉ nhận tài liệu THUỘC booking này — chặn gia sư mượn tài liệu booking khác.
        var bookingMaterials = await materialRepository.GetByBookingIdAsync(bookingId);
        var chosen = bookingMaterials.Where(m => request.MaterialIds.Contains(m.Materialid)).ToList();
        if (chosen.Count == 0)
            throw new MaterialNotFoundException();

        // Hạn mức tính theo BUỔI HỌC: buổi phụ có hạn mức riêng.
        if (request.ClassSessionId is int sessionId)
        {
            var used = await repo.CountQuestionsInSessionAsync(sessionId);
            if (used >= SessionPracticeQuota.MaxQuestionsPerSession)
                throw new PracticeQuotaExceededException(used, SessionPracticeQuota.MaxQuestionsPerSession);
        }

        var contents = await repo.GetMaterialContentsAsync(chosen.Select(m => m.Materialid).ToList());
        var contentByMaterial = contents.ToDictionary(c => c.MaterialId);

        // Tài liệu chưa trích xong thì chưa sinh đề được — báo tên cụ thể để gia sư
        // biết bỏ cái nào ra.
        var notReady = chosen.FirstOrDefault(m =>
            !contentByMaterial.TryGetValue(m.Materialid, out var c) || c.Status != MaterialContentStatus.Ready);
        if (notReady != null)
            throw new MaterialContentNotReadyException(notReady.Title);

        var sources = chosen
            .Select(m => new AiMaterialSource(
                m.Materialid, m.Title, contentByMaterial[m.Materialid].FullText))
            .ToList();

        var generated = await aiClient.GeneratePracticeAsync(sources, request.Prompt);
        if (generated == null || generated.Questions.Count == 0)
        {
            logger.LogWarning("Sinh bài tập thất bại cho booking {BookingId}, tutor {TutorId}: {Reason}",
                bookingId, tutorUserId, generated?.Refusal ?? "(không rõ)");
            // AI từ chối vì yêu cầu lạc đề/không phải ra đề -> nói rõ lý do, gia sư biết
            // đường sửa.
            throw string.IsNullOrWhiteSpace(generated?.Refusal)
                ? new PracticeGenerationFailedException()
                : new PracticeGenerationRefusedException(generated.Refusal);
        }

        var now = TimeZoneHelper.UtcNow;
        var set = new SessionPracticeSet
        {
            BookingId = bookingId,
            ClassSessionId = request.ClassSessionId,
            TutorId = tutorUserId,
            Title = Truncate(generated.Title, 255),
            Prompt = request.Prompt,
            Status = SessionPracticeSetStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var validMaterialIds = chosen.Select(m => m.Materialid).ToHashSet();

        // AI có thể sinh nhiều hơn phần hạn mức còn lại (gia sư gõ "20 câu") -> chỉ
        // nhận đủ phần còn dư, không từ chối cả lượt.
        var remaining = request.ClassSessionId is int sid
            ? SessionPracticeQuota.MaxQuestionsPerSession - await repo.CountQuestionsInSessionAsync(sid)
            : int.MaxValue;

        var order = 1;
        foreach (var q in generated.Questions)
        {
            var format = q.Format == SessionPracticeQuestionFormat.Essay
                ? SessionPracticeQuestionFormat.Essay
                : SessionPracticeQuestionFormat.MultipleChoice;

            var options = q.Options?.Select(o => new AnswerOption { Key = o.Key, Text = o.Text }).ToList();

            // Trắc nghiệm mà AI trả thiếu phương án/đáp án thì BỎ câu đó, không hạ cấp
            // thành tự luận: DB có CHECK chặn, và câu hỏng lọt vào là gia sư phải dọn tay.
            if (format == SessionPracticeQuestionFormat.MultipleChoice)
            {
                if (options == null || options.Count < 2 || string.IsNullOrWhiteSpace(q.CorrectAnswer))
                {
                    logger.LogWarning("Bỏ câu trắc nghiệm thiếu phương án/đáp án do AI sinh (booking {BookingId})", bookingId);
                    continue;
                }
                if (!options.Any(o => o.Key == q.CorrectAnswer))
                {
                    logger.LogWarning("Bỏ câu có correct_answer '{Answer}' không khớp phương án nào (booking {BookingId})",
                        q.CorrectAnswer, bookingId);
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(q.Content))
                continue;
            if (set.Questions.Count >= remaining)
                break;

            set.Questions.Add(new SessionPracticeQuestion
            {
                DisplayOrder = order++,
                QuestionFormat = format,
                Content = q.Content,
                AnswerOptions = format == SessionPracticeQuestionFormat.MultipleChoice ? options : null,
                CorrectAnswer = format == SessionPracticeQuestionFormat.MultipleChoice ? q.CorrectAnswer : null,
                Explanation = q.Explanation,
                // Chỉ nhận material_id AI trả về nếu nằm trong đúng tập đã chọn —
                // tránh FK trỏ lung tung khi AI bịa số.
                SourceMaterialId = q.SourceMaterialId is int id && validMaterialIds.Contains(id) ? id : null,
                SourcePage = q.SourcePage,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        if (set.Questions.Count == 0)
            throw new PracticeGenerationFailedException();

        repo.AddSet(set);
        // Gắn tài liệu nguồn bằng ID
        await repo.LinkMaterialsAsync(set.Id, validMaterialIds);
        await repo.SaveChangesAsync();

        logger.LogInformation("Tutor {TutorId} sinh {Count} câu cho booking {BookingId}",
            tutorUserId, set.Questions.Count, bookingId);

        return MapSet(set, isTutor: true, myAnswers: new Dictionary<Guid, SessionPracticeAnswer>(), materials: chosen);
    }

    public async Task<SessionPracticeQuestionResponse> UpdateQuestionAsync(
        Guid questionId, string tutorUserId, UpdateSessionPracticeQuestionRequest request)
    {
        var question = await repo.GetQuestionAsync(questionId)
            ?? throw new PracticeQuestionNotFoundException();

        EnsureQuestionEditable(question, tutorUserId);

        if (question.QuestionFormat == SessionPracticeQuestionFormat.MultipleChoice)
        {
            if (request.AnswerOptions == null || request.AnswerOptions.Count < 2)
                throw new PracticeQuestionInvalidException("Câu trắc nghiệm cần ít nhất 2 phương án.");
            if (string.IsNullOrWhiteSpace(request.CorrectAnswer)
                || !request.AnswerOptions.Any(o => o.Key == request.CorrectAnswer))
                throw new PracticeQuestionInvalidException("Chọn đáp án đúng trong danh sách phương án.");

            question.AnswerOptions = request.AnswerOptions;
            question.CorrectAnswer = request.CorrectAnswer;
        }

        question.Content = request.Content;
        question.Explanation = request.Explanation;
        question.UpdatedAt = TimeZoneHelper.UtcNow;

        await repo.SaveChangesAsync();

        return MapQuestion(question, revealAnswer: true, myAnswer: null);
    }

    public async Task DeleteQuestionAsync(Guid questionId, string tutorUserId)
    {
        var question = await repo.GetQuestionAsync(questionId)
            ?? throw new PracticeQuestionNotFoundException();

        EnsureQuestionEditable(question, tutorUserId);

        repo.RemoveQuestion(question);
        await repo.SaveChangesAsync();
    }

    public async Task<SessionPracticeQuestionResponse> SendQuestionAsync(Guid questionId, string tutorUserId)
    {
        var question = await repo.GetQuestionAsync(questionId)
            ?? throw new PracticeQuestionNotFoundException();

        var set = question.Set ?? throw new PracticeSetNotFoundException();
        if (set.TutorId != tutorUserId)
            throw new PracticeAccessDeniedException();
        if (question.SentAt != null)
            throw new PracticeSetAlreadySentException();

        var now = TimeZoneHelper.UtcNow;
        question.SentAt = now;
        question.UpdatedAt = now;
        MarkSetSent(set, now);

        await repo.SaveChangesAsync();

        logger.LogInformation("Tutor {TutorId} gửi câu {QuestionId} (bộ {SetId})",
            tutorUserId, questionId, set.Id);

        return MapQuestion(question, revealAnswer: true, myAnswer: null);
    }

    public async Task<SessionPracticeSetResponse> SendAsync(Guid setId, string tutorUserId)
    {
        var set = await repo.GetSetAsync(setId) ?? throw new PracticeSetNotFoundException();

        if (set.TutorId != tutorUserId)
            throw new PracticeAccessDeniedException();
        if (set.Questions.Count == 0)
            throw new PracticeSetEmptyException();

        var pending = set.Questions.Where(q => q.SentAt == null).ToList();
        if (pending.Count == 0)
            throw new PracticeSetAlreadySentException();

        var now = TimeZoneHelper.UtcNow;
        foreach (var question in pending)
        {
            question.SentAt = now;
            question.UpdatedAt = now;
        }
        MarkSetSent(set, now);

        await repo.SaveChangesAsync();

        logger.LogInformation("Tutor {TutorId} gửi {Count} câu còn lại của bộ {SetId}",
            tutorUserId, pending.Count, setId);

        return MapSet(set, isTutor: true, myAnswers: new Dictionary<Guid, SessionPracticeAnswer>());
    }

    /// <summary>
    /// Trạng thái bộ giờ chỉ là TỔNG HỢP: 'sent' = đã gửi ít nhất 1 câu. Nguồn sự thật
    /// cho "học sinh thấy câu nào" là SessionPracticeQuestion.SentAt.
    /// </summary>
    private static void MarkSetSent(SessionPracticeSet set, DateTime now)
    {
        set.Status = SessionPracticeSetStatus.Sent;
        set.SentAt ??= now;
        set.UpdatedAt = now;
    }

    public async Task<SessionPracticeAnswerResponse> SubmitAnswerAsync(
        Guid questionId, string studentUserId, SubmitSessionPracticeAnswerRequest request)
    {
        var question = await repo.GetQuestionAsync(questionId)
            ?? throw new PracticeQuestionNotFoundException();

        var set = question.Set ?? throw new PracticeSetNotFoundException();

        // Chưa gửi thì học sinh không được thấy, càng không được làm. Kiểm tra theo
        // CÂU: bộ có thể đã 'sent' nhưng câu này vẫn còn nháp.
        if (question.SentAt == null)
            throw new PracticeSetNotSentException();

        var booking = await bookingRepository.FindWithStudentAsync(set.BookingId)
            ?? throw new BookingNotFoundException();
        if (!IsStudentOfBooking(booking, studentUserId))
            throw new PracticeAccessDeniedException();

        // Trắc nghiệm chấm cơ học ngay; tự luận để null — gia sư nhận xét miệng.
        bool? isCorrect = question.QuestionFormat == SessionPracticeQuestionFormat.MultipleChoice
            ? string.Equals(request.Answer, question.CorrectAnswer, StringComparison.OrdinalIgnoreCase)
            : null;

        var existing = await repo.GetAnswerAsync(questionId, studentUserId);
        if (existing != null)
        {
            // Làm lại thì GHI ĐÈ — MVP không giữ lịch sử nhiều lần làm.
            existing.Answer = request.Answer;
            existing.IsCorrect = isCorrect;
            existing.AnsweredAt = TimeZoneHelper.UtcNow;
        }
        else
        {
            existing = new SessionPracticeAnswer
            {
                QuestionId = questionId,
                StudentId = studentUserId,
                Answer = request.Answer,
                IsCorrect = isCorrect,
                AnsweredAt = TimeZoneHelper.UtcNow,
            };
            repo.AddAnswer(existing);
        }

        await repo.SaveChangesAsync();

        return new SessionPracticeAnswerResponse
        {
            Answer = existing.Answer,
            IsCorrect = existing.IsCorrect,
            AnsweredAt = existing.AnsweredAt,
            // Đã nộp rồi thì được xem đáp án — trả luôn để FE khỏi gọi lại.
            CorrectAnswer = question.CorrectAnswer,
            Explanation = question.Explanation,
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Chỉ gia sư sở hữu mới thao tác được, và CHỈ khi câu đó chưa gửi — câu đã gửi
    /// thì học sinh có thể đang làm dở, sửa/xoá là mất bài làm.
    /// Khoá theo TỪNG CÂU (không theo bộ) vì gia sư gửi lẻ từng câu.
    /// </summary>
    private static void EnsureQuestionEditable(SessionPracticeQuestion question, string tutorUserId)
    {
        var set = question.Set ?? throw new PracticeSetNotFoundException();
        if (set.TutorId != tutorUserId)
            throw new PracticeAccessDeniedException();
        if (question.SentAt != null)
            throw new PracticeSetAlreadySentException();
    }

    /// <summary>Học sinh của booking có thể đăng nhập bằng chính tài khoản mình hoặc tài khoản liên kết.</summary>
    private static bool IsStudentOfBooking(Booking booking, string userId)
        => booking.Studentid == userId || booking.Student?.Linkeduserid == userId;

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    private static SessionPracticeSetResponse MapSet(
        SessionPracticeSet set,
        bool isTutor,
        IReadOnlyDictionary<Guid, SessionPracticeAnswer> myAnswers,
        IReadOnlyCollection<Learningmaterial>? materials = null)
    {
        // Lúc vừa tạo, set.Materials chưa nạp (gắn bằng ID) nên truyền thẳng vào.
        IReadOnlyCollection<Learningmaterial> sourceMaterials = materials ?? set.Materials.ToList();
        var materialTitles = sourceMaterials.ToDictionary(m => m.Materialid, m => m.Title);

        return new SessionPracticeSetResponse
        {
            Id = set.Id,
            BookingId = set.BookingId,
            ClassSessionId = set.ClassSessionId,
            Title = set.Title,
            Prompt = set.Prompt,
            Status = set.Status,
            SentAt = set.SentAt,
            CreatedAt = set.CreatedAt,
            Materials = sourceMaterials
                .Select(m => new SessionPracticeMaterialRef { MaterialId = m.Materialid, Title = m.Title })
                .ToList(),
            Questions = set.Questions
                // Học sinh CHỈ thấy câu đã gửi — gia sư gửi lẻ nên trong cùng một bộ
                // có thể vừa có câu đã gửi vừa có câu còn nháp.
                .Where(q => isTutor || q.SentAt != null)
                .OrderBy(q => q.DisplayOrder)
                .Select(q =>
                {
                    myAnswers.TryGetValue(q.Id, out var answer);
                    // Học sinh chỉ thấy đáp án SAU khi đã trả lời; gia sư thấy luôn.
                    var reveal = isTutor || answer != null;
                    var mapped = MapQuestion(q, reveal, answer);
                    if (q.SourceMaterialId is int id && materialTitles.TryGetValue(id, out var title))
                        mapped.SourceMaterialTitle = title;
                    return mapped;
                })
                .ToList(),
        };
    }

    private static SessionPracticeQuestionResponse MapQuestion(
        SessionPracticeQuestion q, bool revealAnswer, SessionPracticeAnswer? myAnswer) => new()
    {
        Id = q.Id,
        SetId = q.SetId,
        DisplayOrder = q.DisplayOrder,
        QuestionFormat = q.QuestionFormat,
        Content = q.Content,
        AnswerOptions = q.AnswerOptions,
        CorrectAnswer = revealAnswer ? q.CorrectAnswer : null,
        Explanation = revealAnswer ? q.Explanation : null,
        SourceMaterialId = q.SourceMaterialId,
        SourcePage = q.SourcePage,
        SentAt = q.SentAt,
        MyAnswer = myAnswer == null ? null : new SessionPracticeAnswerResponse
        {
            Answer = myAnswer.Answer,
            IsCorrect = myAnswer.IsCorrect,
            AnsweredAt = myAnswer.AnsweredAt,
        },
    };
}
