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
///
/// BỐI CẢNH ĐIỀU KHIỂN THIẾT KẾ: gia sư đang đứng lớp, mọi thao tác phải nhanh và
/// không được sai. Vì vậy:
///   • AI sinh xong là NHÁP — không tự đến tay học sinh. Gia sư đọc rồi mới gửi.
///   • Bộ đã gửi thì khoá sửa/xoá: học sinh có thể đang làm dở, đổi đề giữa chừng
///     là mất bài làm và làm học sinh rối.
///   • Đáp án đúng + gợi ý bị CHE với học sinh cho tới khi em trả lời câu đó.
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

        // Học sinh: ghép bài làm của chính em vào từng câu (1 truy vấn cho cả trang,
        // không N+1).
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
            logger.LogWarning("Sinh bài tập thất bại cho booking {BookingId}, tutor {TutorId}", bookingId, tutorUserId);
            throw new PracticeGenerationFailedException();
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
        // Gắn tài liệu nguồn bằng ID, KHÔNG add entity Learningmaterial lấy từ
        // GetByBookingIdAsync: chúng là AsNoTracking, attach vào là EF coi như bản ghi
        // mới và cố INSERT lại tài liệu đã tồn tại.
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

        EnsureDraftOwnedByTutor(question.Set, tutorUserId);

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

        EnsureDraftOwnedByTutor(question.Set, tutorUserId);

        repo.RemoveQuestion(question);
        await repo.SaveChangesAsync();
    }

    public async Task<SessionPracticeSetResponse> SendAsync(Guid setId, string tutorUserId)
    {
        var set = await repo.GetSetAsync(setId) ?? throw new PracticeSetNotFoundException();

        if (set.TutorId != tutorUserId)
            throw new PracticeAccessDeniedException();
        if (set.Status == SessionPracticeSetStatus.Sent)
            throw new PracticeSetAlreadySentException();
        if (set.Questions.Count == 0)
            throw new PracticeSetEmptyException();

        set.Status = SessionPracticeSetStatus.Sent;
        set.SentAt = TimeZoneHelper.UtcNow;
        set.UpdatedAt = set.SentAt.Value;

        await repo.SaveChangesAsync();

        logger.LogInformation("Tutor {TutorId} gửi bộ bài tập {SetId} ({Count} câu)",
            tutorUserId, setId, set.Questions.Count);

        return MapSet(set, isTutor: true, myAnswers: new Dictionary<Guid, SessionPracticeAnswer>());
    }

    public async Task<SessionPracticeAnswerResponse> SubmitAnswerAsync(
        Guid questionId, string studentUserId, SubmitSessionPracticeAnswerRequest request)
    {
        var question = await repo.GetQuestionAsync(questionId)
            ?? throw new PracticeQuestionNotFoundException();

        var set = question.Set ?? throw new PracticeSetNotFoundException();

        // Chưa gửi thì học sinh không được thấy, càng không được làm.
        if (set.Status != SessionPracticeSetStatus.Sent)
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
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void EnsureDraftOwnedByTutor(SessionPracticeSet? set, string tutorUserId)
    {
        if (set == null)
            throw new PracticeSetNotFoundException();
        if (set.TutorId != tutorUserId)
            throw new PracticeAccessDeniedException();
        // Đã gửi thì khoá: học sinh có thể đang làm dở.
        if (set.Status == SessionPracticeSetStatus.Sent)
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
        MyAnswer = myAnswer == null ? null : new SessionPracticeAnswerResponse
        {
            Answer = myAnswer.Answer,
            IsCorrect = myAnswer.IsCorrect,
            AnsweredAt = myAnswer.AnsweredAt,
        },
    };
}
