using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using Xunit;

namespace MV.ApplicationLayer.Tests;

/// <summary>
/// Covers what a tutor's suspension does to the courses they were still teaching.
/// </summary>
/// <remarks>
/// Exercised through <c>PreviewCascadeAsync</c>: it shares the session-selection and refund
/// arithmetic with <c>CascadeSuspensionAsync</c>, which additionally takes <c>FOR UPDATE</c> row
/// locks that the in-memory provider cannot execute. The decisions worth pinning down — which
/// sessions a suspension reaches, how much goes back, and when a course is closed outright — all
/// live in the shared half.
/// </remarks>
public class SuspensionRefundCascadeTests
{
    private const string TutorId = "tutor-1";
    private const string ParentId = "parent-1";
    private const string StudentUserId = "student-user-1";

    private static readonly DateTime Now = new(2026, 8, 25, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task TemporarySuspension_OnlyCancelsSessionsInsideTheSuspensionWindow()
    {
        await using var context = CreateContext();
        // 4 sessions × 500k. Two fall inside a 7-day suspension, two land after it.
        SeedBooking(context, sessionOffsetsInDays: new[] { 1, 3, 12, 20 });
        await context.SaveChangesAsync();

        var impact = await CreateService(context).PreviewCascadeAsync(TutorId, Now.AddDays(7));

        Assert.Equal(2, impact.SessionsCancelled);
        Assert.Equal(1_000_000m, impact.TotalRefunded);
        // Sessions survive past the window, so the course keeps going.
        Assert.Equal(0, impact.BookingsClosed);
        Assert.False(impact.Bookings.Single().Closed);
        Assert.Equal(BookingStatus.Ongoing, impact.Bookings.Single().BookingStatus);
    }

    [Fact]
    public async Task PermanentSuspension_CancelsEveryUndeliveredSessionAndClosesTheCourse()
    {
        await using var context = CreateContext();
        SeedBooking(context, sessionOffsetsInDays: new[] { 1, 3, 12, 20 });
        await context.SaveChangesAsync();

        var impact = await CreateService(context).PreviewCascadeAsync(TutorId, suspensionEndDate: null);

        Assert.Equal(4, impact.SessionsCancelled);
        Assert.Equal(2_000_000m, impact.TotalRefunded);
        Assert.Equal(1, impact.BookingsClosed);
        // Nothing was ever delivered, so this is a plain cancellation rather than a completion.
        Assert.Equal(BookingStatus.Cancelled, impact.Bookings.Single().BookingStatus);
    }

    [Fact]
    public async Task DeliveredSessionsAreNeverCancelled_AndTheirCourseClosesAsCompleted()
    {
        await using var context = CreateContext();
        var booking = SeedBooking(context, sessionOffsetsInDays: new[] { -5, -2, 4, 9 });
        // The first two already happened and were settled — the tutor keeps that money.
        foreach (var session in booking.ClassSessions.Take(2))
        {
            session.Status = ClassSessionStatus.Completed;
            session.Issettled = true;
        }
        await context.SaveChangesAsync();

        var impact = await CreateService(context).PreviewCascadeAsync(TutorId, suspensionEndDate: null);

        Assert.Equal(2, impact.SessionsCancelled);
        Assert.Equal(1_000_000m, impact.TotalRefunded);
        Assert.Equal(BookingStatus.Completed, impact.Bookings.Single().BookingStatus);
    }

    [Fact]
    public async Task RefundIsCappedAtWhatTheParentActuallyPaid()
    {
        await using var context = CreateContext();
        var booking = SeedBooking(context, sessionOffsetsInDays: new[] { 1, 3, 12, 20 });
        // Only the deposit phase was collected — the remaining 1.5M was never charged, so a
        // naive "4 sessions × 500k" refund would hand back money that never arrived.
        booking.Remainingpaidat = null;
        booking.Depositamount = 500_000m;
        booking.Depositpaidat = Now.AddDays(-10);
        await context.SaveChangesAsync();

        var impact = await CreateService(context).PreviewCascadeAsync(TutorId, suspensionEndDate: null);

        Assert.Equal(4, impact.SessionsCancelled);
        Assert.Equal(500_000m, impact.TotalRefunded);
    }

    [Fact]
    public async Task EarlierRefundsOnTheSameBookingAreDeducted()
    {
        await using var context = CreateContext();
        SeedBooking(context, sessionOffsetsInDays: new[] { 1, 3, 12, 20 });
        // A dispute already returned one session's worth for this booking.
        context.Wallettransactions.Add(new Wallettransaction
        {
            Walletid = 2,
            Amount = 500_000m,
            Transactiontype = TransactionType.Refund,
            Referencetable = ReferenceTable.Booking,
            Referenceid = 1,
            Createdat = Now.AddDays(-1)
        });
        await context.SaveChangesAsync();

        var impact = await CreateService(context).PreviewCascadeAsync(TutorId, suspensionEndDate: null);

        // 2M collected − 500k already refunded = 1.5M left to give back.
        Assert.Equal(1_500_000m, impact.TotalRefunded);
    }

    [Fact]
    public async Task EscrowReversalNeverExceedsTheTutorsFrozenBalance()
    {
        await using var context = CreateContext();
        SeedBooking(context, sessionOffsetsInDays: new[] { 1, 3, 12, 20 });
        await context.SaveChangesAsync();

        // Escrow for two sessions has already been released elsewhere, leaving less frozen than
        // the four undelivered sessions are nominally worth.
        context.Wallets.Single(w => w.Userid == TutorId).Frozenbalance = 700_000m;
        await context.SaveChangesAsync();

        var impact = await CreateService(context).PreviewCascadeAsync(TutorId, suspensionEndDate: null);

        Assert.Equal(700_000m, impact.TotalEscrowReversed);
        // The parent is still made whole from what was collected, not from the tutor's escrow.
        Assert.Equal(2_000_000m, impact.TotalRefunded);
    }

    [Fact]
    public async Task FrozenBalanceIsSharedAcrossBookings_NotCountedTwice()
    {
        await using var context = CreateContext();
        SeedBooking(context, sessionOffsetsInDays: new[] { 1, 3 }, bookingId: 1);
        SeedBooking(context, sessionOffsetsInDays: new[] { 2, 4 }, bookingId: 2, walletIdOffset: 0);
        await context.SaveChangesAsync();

        // One pot of escrow covering both courses, short of what the four sessions are worth.
        context.Wallets.Single(w => w.Userid == TutorId).Frozenbalance = 600_000m;
        await context.SaveChangesAsync();

        var impact = await CreateService(context).PreviewCascadeAsync(TutorId, suspensionEndDate: null);

        Assert.Equal(2, impact.BookingsAffected);
        Assert.Equal(600_000m, impact.TotalEscrowReversed);
    }

    [Fact]
    public async Task ACourseWithASessionAwaitingConfirmationIsNotClosed()
    {
        await using var context = CreateContext();
        var booking = SeedBooking(context, sessionOffsetsInDays: new[] { -1, 3, 9 });
        // The tutor taught this one and is waiting on the parent — its settlement owns the outcome.
        booking.ClassSessions.First().Status = ClassSessionStatus.PendingConfirmation;
        await context.SaveChangesAsync();

        var impact = await CreateService(context).PreviewCascadeAsync(TutorId, suspensionEndDate: null);

        Assert.Equal(2, impact.SessionsCancelled);
        Assert.Equal(0, impact.BookingsClosed);
        Assert.False(impact.Bookings.Single().Closed);
    }

    [Fact]
    public async Task ASelfBookedStudentIsRefundedToTheirOwnWallet()
    {
        await using var context = CreateContext();
        var booking = SeedBooking(context, sessionOffsetsInDays: new[] { 1, 3 });
        await context.SaveChangesAsync();

        // No parent on the booking: the student signed up and paid for themselves.
        booking.Parentid = null;
        await context.SaveChangesAsync();

        var impact = await CreateService(context).PreviewCascadeAsync(TutorId, suspensionEndDate: null);

        Assert.Equal(1, impact.BookingsAffected);
        Assert.Empty(impact.BookingsNeedingManualReview);
        Assert.Equal(StudentUserId, impact.Bookings.Single().RefundRecipientId);
    }

    [Fact]
    public async Task ALegacyProfileWithoutLinkeduseridFallsBackToItsMatchingAccount()
    {
        await using var context = CreateContext();
        var booking = SeedBooking(context, sessionOffsetsInDays: new[] { 1, 3 });
        await context.SaveChangesAsync();

        // Self-registered students predating Linkeduserid: the profile key IS the account id,
        // and there is nothing else on the row to resolve the payer from.
        context.Studentprofiles.Add(new Studentprofile
        {
            Studentid = StudentUserId,
            Parentid = null,
            Linkeduserid = null,
            Fullname = "Học sinh cũ"
        });
        booking.Parentid = null;
        booking.Studentid = StudentUserId;
        await context.SaveChangesAsync();

        var impact = await CreateService(context).PreviewCascadeAsync(TutorId, suspensionEndDate: null);

        Assert.Equal(1, impact.BookingsAffected);
        Assert.Empty(impact.BookingsNeedingManualReview);
        Assert.Equal(StudentUserId, impact.Bookings.Single().RefundRecipientId);
    }

    [Fact]
    public async Task ABookingWithNoPayerAccountIsFlaggedInsteadOfRefunded()
    {
        await using var context = CreateContext();
        var booking = SeedBooking(context, sessionOffsetsInDays: new[] { 1, 3 });
        await context.SaveChangesAsync();

        // A self-booked student whose profile has no linked user account: Booking.Studentid points
        // at studentprofiles, not users, so there is no wallet to credit.
        booking.Parentid = null;
        context.Studentprofiles.Single().Linkeduserid = null;
        await context.SaveChangesAsync();

        var impact = await CreateService(context).PreviewCascadeAsync(TutorId, suspensionEndDate: null);

        Assert.Equal(0, impact.BookingsAffected);
        Assert.Equal(0m, impact.TotalRefunded);
        Assert.Equal(new[] { 1 }, impact.BookingsNeedingManualReview);
    }

    [Fact]
    public async Task AlreadyFinishedCoursesAreLeftAlone()
    {
        await using var context = CreateContext();
        var booking = SeedBooking(context, sessionOffsetsInDays: new[] { 1, 3 });
        booking.Status = BookingStatus.Completed;
        await context.SaveChangesAsync();

        var impact = await CreateService(context).PreviewCascadeAsync(TutorId, suspensionEndDate: null);

        Assert.Equal(0, impact.BookingsAffected);
        Assert.Empty(impact.Bookings);
    }

    // ─── Fixture ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A live 4-session course worth 2,000,000đ to the parent (500k/session) with 1,600,000đ of
    /// tutor escrow frozen (400k/session, after the platform fee).
    /// </summary>
    private static Booking SeedBooking(
        AgoraDbContext context,
        int[] sessionOffsetsInDays,
        int bookingId = 1,
        int walletIdOffset = 1)
    {
        var sessionCount = sessionOffsetsInDays.Length;
        var parentPaid = 500_000m * sessionCount;

        if (walletIdOffset > 0)
        {
            // Real accounts, because payer resolution falls back to the profile key only after
            // confirming a matching user row exists.
            context.Users.AddRange(
                NewUser(TutorId, UserRole.Tutor),
                NewUser(ParentId, UserRole.Parent),
                NewUser(StudentUserId, UserRole.Student));
            context.Wallets.Add(new Wallet { Walletid = 1, Userid = TutorId, Balance = 0m, Frozenbalance = 400_000m * sessionCount });
            context.Wallets.Add(new Wallet { Walletid = 2, Userid = ParentId, Balance = 0m, Frozenbalance = 0m });
            context.Studentprofiles.Add(new Studentprofile
            {
                Studentid = "student-profile-1",
                Parentid = ParentId,
                Linkeduserid = StudentUserId,
                Fullname = "Học sinh"
            });
        }

        var booking = new Booking
        {
            Bookingid = bookingId,
            Parentid = ParentId,
            Studentid = "student-profile-1",
            Tutorid = TutorId,
            Status = BookingStatus.Ongoing,
            Totalsessions = sessionCount,
            Sessionsremaining = sessionCount,
            Finalprice = parentPaid,
            Tutorfee = 400_000m * sessionCount,
            Depositamount = 500_000m,
            Depositpaidat = Now.AddDays(-10),
            Remainingpaidat = Now.AddDays(-9),
            Escrowstatus = EscrowStatus.Holding
        };

        for (var i = 0; i < sessionCount; i++)
        {
            booking.ClassSessions.Add(new ClassSession
            {
                Classsessionid = bookingId * 100 + i,
                Bookingid = bookingId,
                Tutorid = TutorId,
                Studentid = "student-profile-1",
                Scheduledstart = Now.AddDays(sessionOffsetsInDays[i]),
                Scheduledend = Now.AddDays(sessionOffsetsInDays[i]).AddHours(2),
                Status = ClassSessionStatus.Scheduled,
                Lessonprice = 500_000m
            });
        }

        context.Bookings.Add(booking);
        return booking;
    }

    private static User NewUser(string id, string role) => new()
    {
        Userid = id,
        Username = id,
        Password = "test-hash",
        Email = $"{id}@test.local",
        Fullname = id,
        Primaryrole = role,
        Status = 1,
        Createdat = Now
    };

    private static SuspensionRefundService CreateService(AgoraDbContext context) =>
        new(context, null!, NullLogger<SuspensionRefundService>.Instance);

    private static AgoraDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new CascadeTestDbContext(options);
    }

    private sealed class CascadeTestDbContext(DbContextOptions<AgoraDbContext> options)
        : AgoraDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // pgvector columns have no in-memory equivalent.
            modelBuilder.Entity<QuestionBank>().Ignore(question => question.Embedding);
            modelBuilder.Entity<TutoraKbChunk>().Ignore(chunk => chunk.Embedding);
        }
    }
}
