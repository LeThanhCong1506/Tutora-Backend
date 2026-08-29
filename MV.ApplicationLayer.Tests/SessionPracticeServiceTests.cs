using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.Services;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using MV.InfrastructureLayer.DBContext;
using MV.InfrastructureLayer.Repositories;
using Xunit;

namespace MV.ApplicationLayer.Tests;

/// <summary>
/// Bài tập nhanh trong buổi học. Tập trung vào các quy tắc mà sai là hỏng sản phẩm:
/// che đáp án với học sinh, khoá bộ đã gửi, và lọc câu hỏng do AI sinh.
/// </summary>
public class SessionPracticeServiceTests
{
    private const int BookingId = 1;
    private const string TutorId = "tutor-1";
    private const string StudentId = "student-1";
    private const string OtherTutorId = "tutor-2";

    // ── Che đáp án ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Student_KhongThayDapAn_TruocKhiTraLoi()
    {
        await using var db = CreateContext();
        Seed(db);
        var set = SeedSentSet(db);
        await db.SaveChangesAsync();

        var sets = await CreateService(db).GetSetsAsync(BookingId, StudentId);

        var question = Assert.Single(sets).Questions.First();
        // Lộ đáp án ở API là học sinh mở DevTools ra xem, bài tập thành vô nghĩa.
        Assert.Null(question.CorrectAnswer);
        Assert.Null(question.Explanation);
        // Phương án vẫn phải trả để em còn chọn.
        Assert.NotNull(question.AnswerOptions);
    }

    [Fact]
    public async Task Student_ThayDapAn_SauKhiTraLoi()
    {
        await using var db = CreateContext();
        Seed(db);
        var set = SeedSentSet(db);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var questionId = set.Questions.First().Id;
        await service.SubmitAnswerAsync(questionId, StudentId,
            new SubmitSessionPracticeAnswerRequest { Answer = "B" });

        var sets = await service.GetSetsAsync(BookingId, StudentId);
        var question = Assert.Single(sets).Questions.First();

        Assert.Equal("A", question.CorrectAnswer);
        Assert.NotNull(question.Explanation);
        Assert.NotNull(question.MyAnswer);
        Assert.False(question.MyAnswer!.IsCorrect);
    }

    [Fact]
    public async Task Tutor_LuonThayDapAn()
    {
        await using var db = CreateContext();
        Seed(db);
        SeedSentSet(db);
        await db.SaveChangesAsync();

        var sets = await CreateService(db).GetSetsAsync(BookingId, TutorId);

        Assert.Equal("A", Assert.Single(sets).Questions.First().CorrectAnswer);
    }

    // ── Phạm vi hiển thị ─────────────────────────────────────────────────────

    [Fact]
    public async Task Student_KhongThayBoNhap()
    {
        await using var db = CreateContext();
        Seed(db);
        SeedDraftSet(db);
        await db.SaveChangesAsync();

        var service = CreateService(db);

        // Bộ nháp là bản gia sư đang duyệt — chưa chốt thì học sinh không được thấy.
        Assert.Empty(await service.GetSetsAsync(BookingId, StudentId));
        Assert.Single(await service.GetSetsAsync(BookingId, TutorId));
    }

    [Fact]
    public async Task Student_KhongLamDuocBaiChuaGui()
    {
        await using var db = CreateContext();
        Seed(db);
        var set = SeedDraftSet(db);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<PracticeSetNotSentException>(() =>
            CreateService(db).SubmitAnswerAsync(set.Questions.First().Id, StudentId,
                new SubmitSessionPracticeAnswerRequest { Answer = "A" }));
    }

    // ── Khoá bộ đã gửi ───────────────────────────────────────────────────────

    [Fact]
    public async Task KhongSuaDuocCauCuaBoDaGui()
    {
        await using var db = CreateContext();
        Seed(db);
        var set = SeedSentSet(db);
        await db.SaveChangesAsync();

        // Học sinh có thể đang làm dở — đổi đề giữa chừng là mất bài làm.
        await Assert.ThrowsAsync<PracticeSetAlreadySentException>(() =>
            CreateService(db).UpdateQuestionAsync(set.Questions.First().Id, TutorId,
                new UpdateSessionPracticeQuestionRequest { Content = "Đề mới" }));
    }

    [Fact]
    public async Task KhongXoaDuocCauCuaBoDaGui()
    {
        await using var db = CreateContext();
        Seed(db);
        var set = SeedSentSet(db);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<PracticeSetAlreadySentException>(() =>
            CreateService(db).DeleteQuestionAsync(set.Questions.First().Id, TutorId));
    }

    [Fact]
    public async Task KhongGuiLaiBoDaGui()
    {
        await using var db = CreateContext();
        Seed(db);
        var set = SeedSentSet(db);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<PracticeSetAlreadySentException>(() =>
            CreateService(db).SendAsync(set.Id, TutorId));
    }

    // ── Quyền ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GiaSuKhac_KhongSuaDuocCau()
    {
        await using var db = CreateContext();
        Seed(db);
        var set = SeedDraftSet(db);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<PracticeAccessDeniedException>(() =>
            CreateService(db).UpdateQuestionAsync(set.Questions.First().Id, OtherTutorId,
                new UpdateSessionPracticeQuestionRequest { Content = "x" }));
    }

    [Fact]
    public async Task NguoiLa_KhongXemDuocBaiTap()
    {
        await using var db = CreateContext();
        Seed(db);
        SeedSentSet(db);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<PracticeAccessDeniedException>(() =>
            CreateService(db).GetSetsAsync(BookingId, "nguoi-la"));
    }

    // ── Chấm và ghi đè ───────────────────────────────────────────────────────

    [Fact]
    public async Task TracNghiem_ChamNgay_TuLuan_KhongCham()
    {
        await using var db = CreateContext();
        Seed(db);
        var set = SeedSentSet(db, withEssay: true);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var mc = await service.SubmitAnswerAsync(set.Questions.First(q => q.QuestionFormat == "mc").Id,
            StudentId, new SubmitSessionPracticeAnswerRequest { Answer = "A" });
        Assert.True(mc.IsCorrect);

        // Tự luận: gia sư nhận xét miệng, hệ thống không chấm.
        var essay = await service.SubmitAnswerAsync(set.Questions.First(q => q.QuestionFormat == "essay").Id,
            StudentId, new SubmitSessionPracticeAnswerRequest { Answer = "Em trình bày..." });
        Assert.Null(essay.IsCorrect);
    }

    [Fact]
    public async Task LamLai_GhiDe_KhongTaoDongMoi()
    {
        await using var db = CreateContext();
        Seed(db);
        var set = SeedSentSet(db);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var questionId = set.Questions.First().Id;

        await service.SubmitAnswerAsync(questionId, StudentId, new SubmitSessionPracticeAnswerRequest { Answer = "B" });
        var second = await service.SubmitAnswerAsync(questionId, StudentId, new SubmitSessionPracticeAnswerRequest { Answer = "A" });

        Assert.True(second.IsCorrect);
        Assert.Equal(1, await db.SessionPracticeAnswers.CountAsync(a => a.QuestionId == questionId));
    }

    // ── Lọc câu hỏng do AI sinh ──────────────────────────────────────────────

    [Fact]
    public async Task BoQuaCauTracNghiemThieuDapAn()
    {
        await using var db = CreateContext();
        Seed(db);
        SeedMaterialWithContent(db);
        await db.SaveChangesAsync();

        var ai = new StubAiClient(new AiGeneratedPractice("Ôn tập", [
            // Hợp lệ
            new AiGeneratedQuestion("mc", "Câu tốt", [new AiAnswerOption("A", "1"), new AiAnswerOption("B", "2")], "A", null, 10, 3),
            // Thiếu correct_answer -> DB có CHECK chặn, phải loại từ service.
            new AiGeneratedQuestion("mc", "Thiếu đáp án", [new AiAnswerOption("A", "1"), new AiAnswerOption("B", "2")], null, null, 10, 4),
            // correct_answer trỏ phương án không tồn tại -> chấm kiểu gì cũng sai.
            new AiGeneratedQuestion("mc", "Đáp án lạ", [new AiAnswerOption("A", "1")], "Z", null, 10, 5),
        ]));

        var result = await CreateService(db, ai).GenerateAsync(BookingId, TutorId,
            new GenerateSessionPracticeRequest { MaterialIds = [10], Prompt = "5 câu" });

        var question = Assert.Single(result.Questions);
        Assert.Equal("Câu tốt", question.Content);
        Assert.Equal(SessionPracticeSetStatus.Draft, result.Status);
    }

    [Fact]
    public async Task BoQuaSourceMaterialIdAiBia()
    {
        await using var db = CreateContext();
        Seed(db);
        SeedMaterialWithContent(db);
        await db.SaveChangesAsync();

        // AI trả material_id 999 không nằm trong tập gia sư chọn -> nhận vào là FK lỗi.
        var ai = new StubAiClient(new AiGeneratedPractice("Ôn tập", [
            new AiGeneratedQuestion("mc", "Câu", [new AiAnswerOption("A", "1"), new AiAnswerOption("B", "2")], "A", null, 999, 3),
        ]));

        var result = await CreateService(db, ai).GenerateAsync(BookingId, TutorId,
            new GenerateSessionPracticeRequest { MaterialIds = [10], Prompt = "1 câu" });

        Assert.Null(Assert.Single(result.Questions).SourceMaterialId);
    }

    [Fact]
    public async Task TaiLieuChuaTrichXong_KhongSinhDuocDe()
    {
        await using var db = CreateContext();
        Seed(db);
        SeedMaterialWithContent(db, status: MaterialContentStatus.Processing);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<MaterialContentNotReadyException>(() =>
            CreateService(db).GenerateAsync(BookingId, TutorId,
                new GenerateSessionPracticeRequest { MaterialIds = [10], Prompt = "5 câu" }));
    }

    [Fact]
    public async Task TaiLieuCuaBookingKhac_BiTuChoi()
    {
        await using var db = CreateContext();
        Seed(db);
        SeedMaterialWithContent(db);
        // Tài liệu 20 thuộc booking khác — gia sư không được mượn sang.
        db.Learningmaterials.Add(new Learningmaterial
        {
            Materialid = 20, Bookingid = 99, Title = "Tài liệu booking khác",
            Ownertype = "tutor", Fileurl = "x", Filetype = "pdf",
        });
        db.LearningMaterialContents.Add(new LearningMaterialContent
        {
            MaterialId = 20, FullText = "abc", Status = MaterialContentStatus.Ready,
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<MaterialNotFoundException>(() =>
            CreateService(db).GenerateAsync(BookingId, TutorId,
                new GenerateSessionPracticeRequest { MaterialIds = [20], Prompt = "5 câu" }));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static AgoraDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseInMemoryDatabase($"session-practice-{Guid.NewGuid()}")
            .Options;
        return new PracticeTestDbContext(options);
    }

    private static SessionPracticeService CreateService(AgoraDbContext db, ITutorAiClient? ai = null)
        => new(
            new SessionPracticeRepository(db),
            new BookingRepository(db),
            new LearningMaterialRepository(db),
            ai ?? new StubAiClient(null),
            NullLogger<SessionPracticeService>.Instance);

    private static void Seed(AgoraDbContext db)
    {
        db.Users.AddRange(
            NewUser(TutorId, UserRole.Tutor),
            NewUser(StudentId, UserRole.Student),
            NewUser(OtherTutorId, UserRole.Tutor));

        db.Bookings.Add(new Booking
        {
            Bookingid = BookingId,
            Tutorid = TutorId,
            Studentid = StudentId,
            Status = BookingStatus.Paid,
        });
    }

    private static void SeedMaterialWithContent(AgoraDbContext db, string status = MaterialContentStatus.Ready)
    {
        db.Learningmaterials.Add(new Learningmaterial
        {
            Materialid = 10, Bookingid = BookingId, Title = "Slide chương 1",
            Ownertype = "tutor", Fileurl = "x", Filetype = "pdf",
        });
        db.LearningMaterialContents.Add(new LearningMaterialContent
        {
            MaterialId = 10, FullText = "[trang 3] Đạo hàm...", Status = status, PageCount = 24,
        });
    }

    private static SessionPracticeSet SeedDraftSet(AgoraDbContext db)
        => SeedSet(db, SessionPracticeSetStatus.Draft, withEssay: false);

    private static SessionPracticeSet SeedSentSet(AgoraDbContext db, bool withEssay = false)
        => SeedSet(db, SessionPracticeSetStatus.Sent, withEssay);

    private static SessionPracticeSet SeedSet(AgoraDbContext db, string status, bool withEssay)
    {
        var now = TimeZoneHelper.UtcNow;
        var set = new SessionPracticeSet
        {
            Id = Guid.NewGuid(),
            BookingId = BookingId,
            TutorId = TutorId,
            Title = "Ôn tập chương 1",
            Status = status,
            SentAt = status == SessionPracticeSetStatus.Sent ? now : null,
            CreatedAt = now,
            UpdatedAt = now,
        };

        set.Questions.Add(new SessionPracticeQuestion
        {
            Id = Guid.NewGuid(),
            DisplayOrder = 1,
            QuestionFormat = SessionPracticeQuestionFormat.MultipleChoice,
            Content = "Đạo hàm của $x^2$ là:",
            AnswerOptions = [new AnswerOption { Key = "A", Text = "$2x$" }, new AnswerOption { Key = "B", Text = "$x$" }],
            CorrectAnswer = "A",
            Explanation = "Quy tắc luỹ thừa.",
            CreatedAt = now,
            UpdatedAt = now,
        });

        if (withEssay)
        {
            set.Questions.Add(new SessionPracticeQuestion
            {
                Id = Guid.NewGuid(),
                DisplayOrder = 2,
                QuestionFormat = SessionPracticeQuestionFormat.Essay,
                Content = "Trình bày quy tắc chuỗi.",
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        db.SessionPracticeSets.Add(set);
        return set;
    }

    private static User NewUser(string id, string role) => new()
    {
        Userid = id,
        Username = id,
        Password = "test",
        Email = $"{id}@test.local",
        Fullname = id,
        Primaryrole = role,
    };

    /// <summary>Trả kết quả dựng sẵn — không gọi mạng.</summary>
    private sealed class StubAiClient(AiGeneratedPractice? result) : ITutorAiClient
    {
        public Task<AiGeneratedPractice?> GeneratePracticeAsync(
            IReadOnlyList<AiMaterialSource> materials, string prompt, CancellationToken cancellationToken = default)
            => Task.FromResult(result);

        public Task<AiMaterialExtraction?> ExtractMaterialAsync(byte[] fileBytes, string fileName, CancellationToken cancellationToken = default)
            => Task.FromResult<AiMaterialExtraction?>(null);

        public Task<List<AiRankedTutor>?> RankAsync(string? query, IReadOnlyList<string> candidateIds, int topK, CancellationToken cancellationToken = default)
            => Task.FromResult<List<AiRankedTutor>?>(null);

        public Task<float[]?> EmbedAsync(string id, string text, CancellationToken cancellationToken = default)
            => Task.FromResult<float[]?>(null);

        public Task EmbedTutorAsync(string tutorId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<List<AiExtractedQuestion>?> ExtractPdfAsync(byte[] pdfBytes, string fileName, CancellationToken cancellationToken = default)
            => Task.FromResult<List<AiExtractedQuestion>?>(null);

        public Task<KbUploadResult?> KbUploadAsync(byte[] fileBytes, string fileName, string? uploadedBy, CancellationToken cancellationToken = default)
            => Task.FromResult<KbUploadResult?>(null);

        public Task<int?> KbUpdateContentAsync(string documentId, string content, CancellationToken cancellationToken = default)
            => Task.FromResult<int?>(null);

        public Task<List<AiSimilarQuestion>> FindSimilarQuestionsAsync(string text, string? chapter, string? difficulty,
            IReadOnlyList<Guid> excludeIds, int topK, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<AiSimilarQuestion>());
    }

    private sealed class PracticeTestDbContext(DbContextOptions<AgoraDbContext> options) : AgoraDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // InMemory không hỗ trợ pgvector.
            modelBuilder.Entity<QuestionBank>().Ignore(x => x.Embedding);
            modelBuilder.Entity<TutoraKbChunk>().Ignore(x => x.Embedding);
        }
    }
}
