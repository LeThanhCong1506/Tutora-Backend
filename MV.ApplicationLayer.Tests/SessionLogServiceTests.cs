using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Configuration;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using System.Text.Json;
using Xunit;

namespace MV.ApplicationLayer.Tests;

/// <summary>
/// The session log is evidence used to decide refunds and tutor penalties, so these tests pin the
/// arithmetic (how long both sides were actually in the room) and the honesty rules (a missing
/// signal must never read as proof of absence).
/// </summary>
public class SessionLogServiceTests
{
    private const int ClassSessionId = 4821;
    private const string TutorUserId = "usr_tutor";
    private const string StudentUserId = "usr_student";
    private const string ParentUserId = "usr_parent";
    private const string StudentProfileId = "stu_1";

    private static readonly DateTime ScheduledStart = new(2026, 7, 23, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ScheduledEnd = new(2026, 7, 23, 13, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task OverlapCountsOnlyTimeBothSidesWerePresent()
    {
        await using var db = CreateContext();
        SeedSession(db);
        SeedParticipants(db, ScheduledStart, ScheduledStart.AddMinutes(10));

        // Tutor 12:00-13:00, student 12:10-13:00 => 50 minutes together.
        AddJoin(db, "101", TutorUserId, ScheduledStart);
        AddJoin(db, "202", StudentUserId, ScheduledStart.AddMinutes(10));
        AddLeave(db, "202", StudentUserId, ScheduledEnd, reason: 1, duration: 3000);
        AddLeave(db, "101", TutorUserId, ScheduledEnd, reason: 1, duration: 3600);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.NotNull(log);
        Assert.Equal(3600, log!.Summary.TutorSeconds);
        Assert.Equal(3000, log.Summary.StudentSeconds);
        Assert.Equal(3000, log.Summary.OverlapSeconds);
        Assert.Equal(0.8333, log.Summary.OverlapRatio, 4);
        Assert.Equal(0, log.Summary.SuggestedRefundPercentage);
        Assert.DoesNotContain(SessionLogFlag.ZeroOverlap, log.Flags);
    }

    [Fact]
    public async Task NetworkDropIsExcludedFromOverlapAndFlagged()
    {
        await using var db = CreateContext();
        SeedSession(db);
        SeedParticipants(db, ScheduledStart, ScheduledStart);

        AddJoin(db, "101", TutorUserId, ScheduledStart);
        AddJoin(db, "202", StudentUserId, ScheduledStart);
        // Tutor drops for 10 minutes on a connection timeout, then comes back.
        AddLeave(db, "101", TutorUserId, ScheduledStart.AddMinutes(20), reason: 2, duration: 1200);
        AddJoin(db, "101", TutorUserId, ScheduledStart.AddMinutes(30));
        AddLeave(db, "101", TutorUserId, ScheduledEnd, reason: 1, duration: 1800);
        AddLeave(db, "202", StudentUserId, ScheduledEnd, reason: 1, duration: 3600);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.NotNull(log);
        Assert.Equal(3000, log!.Summary.TutorSeconds);
        Assert.Equal(3000, log.Summary.OverlapSeconds);
        Assert.Contains(SessionLogFlag.NetworkDrop, log.Flags);

        var tutor = Assert.Single(log.Participants, p => p.Role == SessionParticipantRole.Tutor);
        Assert.Equal(1, tutor.DropCount);
        Assert.Equal(2, tutor.JoinCount);
        Assert.Contains(tutor.Disconnects, d => d.Involuntary && d.Reason == AgoraLeaveReason.ConnectionTimeout);
    }

    [Fact]
    public async Task VoluntaryLeaveIsNotCountedAsADrop()
    {
        await using var db = CreateContext();
        SeedSession(db);
        SeedParticipants(db, ScheduledStart, studentJoinsAt: null);

        AddJoin(db, "101", TutorUserId, ScheduledStart);
        AddLeave(db, "101", TutorUserId, ScheduledEnd, reason: 1, duration: 3600);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        var tutor = Assert.Single(log!.Participants, p => p.Role == SessionParticipantRole.Tutor);
        Assert.Equal(0, tutor.DropCount);
        Assert.DoesNotContain(SessionLogFlag.NetworkDrop, log.Flags);
    }

    [Fact]
    public async Task JoinWithoutLeaveIsClosedAtChannelDestroyAndFlagged()
    {
        await using var db = CreateContext();
        SeedSession(db);
        SeedParticipants(db, ScheduledStart, ScheduledStart);

        AddJoin(db, "101", TutorUserId, ScheduledStart);
        AddJoin(db, "202", StudentUserId, ScheduledStart);
        AddChannelEvent(db, AgoraChannelEventType.ChannelDestroy, ScheduledEnd, uid: null);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.NotNull(log);
        Assert.Contains(SessionLogFlag.UnclosedInterval, log!.Flags);
        Assert.Equal(3600, log.Summary.OverlapSeconds);
    }

    [Fact]
    public async Task ChannelDestroySplitsPresenceBeforeALaterRejoin()
    {
        await using var db = CreateContext();
        SeedSession(db);

        AddJoin(db, "101", TutorUserId, ScheduledStart);
        AddChannelEvent(
            db,
            AgoraChannelEventType.ChannelDestroy,
            ScheduledStart.AddMinutes(20),
            uid: null);
        AddJoin(db, "101", TutorUserId, ScheduledStart.AddMinutes(30));
        AddLeave(
            db,
            "101",
            TutorUserId,
            ScheduledEnd,
            reason: AgoraLeaveReason.Quit,
            duration: 1800);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);
        var tutor = Assert.Single(log!.Participants, p => p.Role == SessionParticipantRole.Tutor);

        Assert.Equal(2, tutor.JoinCount);
        Assert.Equal(2, tutor.Intervals.Count);
        Assert.Equal(ScheduledStart.AddMinutes(20), tutor.Intervals[0].End);
        Assert.Equal(ScheduledStart.AddMinutes(30), tutor.Intervals[1].Start);
        Assert.Equal(3000, tutor.TotalSeconds);
        Assert.Contains(SessionLogFlag.UnclosedInterval, log.Flags);
        Assert.False(log.Summary.IsEvidenceConclusive);
    }

    [Fact]
    public async Task DestroyedInProgressRoomStaysJoinableButHasNoCurrentParticipant()
    {
        await using var db = CreateContext();
        SeedSession(db, status: ClassSessionStatus.InProgress);

        AddJoin(db, "101", TutorUserId, ScheduledStart);
        var destroyedAt = ScheduledStart.AddMinutes(20);
        AddChannelEvent(db, AgoraChannelEventType.ChannelDestroy, destroyedAt, uid: null);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);
        var tutor = Assert.Single(log!.Participants, p => p.Role == SessionParticipantRole.Tutor);

        // App policy still permits rejoin while status is in_progress and checkout is null, so the
        // snapshot remains provisional even though this channel epoch has ended.
        Assert.True(log.Summary.IsOngoing);
        Assert.False(log.Summary.IsEvidenceConclusive);
        Assert.Null(log.Summary.SuggestedRefundPercentage);
        Assert.False(tutor.IsCurrentlyPresent);
        Assert.Equal(destroyedAt, tutor.LastLeaveAt);
        Assert.Equal(destroyedAt, tutor.Intervals[^1].End);
    }

    [Fact]
    public async Task LeaveWithoutJoinRebuildsTheIntervalFromReportedDuration()
    {
        await using var db = CreateContext();
        SeedSession(db);
        SeedParticipants(db, ScheduledStart, studentJoinsAt: null);

        // The join notification never arrived; Agora still reports how long the user was present.
        AddLeave(db, "101", TutorUserId, ScheduledStart.AddMinutes(30), reason: 1, duration: 1800);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        var tutor = Assert.Single(log!.Participants, p => p.Role == SessionParticipantRole.Tutor);
        Assert.Equal(1800, tutor.TotalSeconds);
        Assert.Equal(ScheduledStart, tutor.FirstJoinAt);
    }

    [Fact]
    public async Task LeaveWithoutJoinOrUsableDurationCannotSupportARefundConclusion()
    {
        await using var db = CreateContext();
        SeedSession(db);

        AddChannelEvent(
            db,
            AgoraChannelEventType.UserLeave,
            ScheduledEnd,
            uid: "101",
            reason: AgoraLeaveReason.Quit,
            duration: null,
            account: TutorUserId);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.Contains(SessionLogFlag.InsufficientEvidence, log!.Flags);
        Assert.False(log.Summary.IsEvidenceConclusive);
        Assert.Null(log.Summary.SuggestedRefundPercentage);
        Assert.DoesNotContain(SessionLogFlag.TutorNeverJoined, log.Flags);
    }

    [Fact]
    public async Task ConsecutiveJoinsExposeTheMissingLeaveAndCountBothAdmissions()
    {
        await using var db = CreateContext();
        SeedSession(db);

        AddJoin(db, "101", TutorUserId, ScheduledStart, clientSequence: 1);
        AddJoin(db, "101", TutorUserId, ScheduledStart.AddMinutes(10), clientSequence: 2);
        AddLeave(
            db,
            "101",
            TutorUserId,
            ScheduledEnd,
            reason: AgoraLeaveReason.Quit,
            duration: 3000,
            clientSequence: 3);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);
        var tutor = Assert.Single(log!.Participants, p => p.Role == SessionParticipantRole.Tutor);

        Assert.Equal(2, tutor.JoinCount);
        Assert.Contains(SessionLogFlag.UnclosedInterval, log.Flags);
        Assert.False(log.Summary.IsEvidenceConclusive);
        Assert.Null(log.Summary.SuggestedRefundPercentage);
    }

    [Fact]
    public async Task EventsStoredOutOfOrderStillProduceTheSameTotals()
    {
        await using var db = CreateContext();
        SeedSession(db);
        SeedParticipants(db, ScheduledStart, studentJoinsAt: null);

        // Inserted leave-before-join on purpose: Agora does not guarantee delivery order.
        AddLeave(db, "101", TutorUserId, ScheduledEnd, reason: 1, duration: 3600);
        AddJoin(db, "101", TutorUserId, ScheduledStart);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        var tutor = Assert.Single(log!.Participants, p => p.Role == SessionParticipantRole.Tutor);
        Assert.Equal(3600, tutor.TotalSeconds);
        Assert.Equal(1, tutor.JoinCount);
    }

    [Fact]
    public async Task NumericUidIsBoundToAParticipantByAdmissionTime()
    {
        await using var db = CreateContext();
        SeedSession(db);
        // Tutor was admitted first, student four minutes later.
        db.SessionParticipants.AddRange(
            Participant(TutorUserId, SessionParticipantRole.Tutor, ScheduledStart.AddSeconds(-5)),
            Participant(StudentUserId, SessionParticipantRole.Student, ScheduledStart.AddMinutes(4)));

        // Agora reports its own numeric ids, which match no account id we know.
        AddJoin(db, "2846271", null, ScheduledStart);
        AddJoin(db, "9931104", null, ScheduledStart.AddMinutes(4).AddSeconds(6));
        AddLeave(db, "9931104", null, ScheduledEnd, reason: 1, duration: 3354);
        AddLeave(db, "2846271", null, ScheduledEnd, reason: 1, duration: 3600);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.NotNull(log);
        var tutor = Assert.Single(log!.Participants, p => p.Role == SessionParticipantRole.Tutor);
        var student = Assert.Single(log.Participants, p => p.Role == SessionParticipantRole.Student);

        Assert.Equal("2846271", tutor.AgoraUid);
        Assert.Equal("9931104", student.AgoraUid);
        Assert.Equal(SessionLogIdentityConfidence.Correlated, tutor.IdentityConfidence);
        Assert.Contains(SessionLogFlag.IdentityUncertain, log.Flags);
        Assert.Equal(3354, log.Summary.OverlapSeconds);
    }

    [Fact]
    public async Task StringUidMatchingOurAccountIsBoundExactlyWithoutAnUncertaintyFlag()
    {
        await using var db = CreateContext();
        SeedSession(db);
        SeedParticipants(db, ScheduledStart, studentJoinsAt: null);

        AddJoin(db, TutorUserId, TutorUserId, ScheduledStart);
        AddLeave(db, TutorUserId, TutorUserId, ScheduledEnd, reason: 1, duration: 3600);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        var tutor = Assert.Single(log!.Participants, p => p.Role == SessionParticipantRole.Tutor);
        Assert.Equal(SessionLogIdentityConfidence.Exact, tutor.IdentityConfidence);
        Assert.DoesNotContain(SessionLogFlag.IdentityUncertain, log.Flags);
    }

    [Fact]
    public async Task NoEventsIsReportedAsMissingDataNotAsNobodyAttending()
    {
        await using var db = CreateContext();
        SeedSession(db);
        SeedParticipants(db, ScheduledStart, studentJoinsAt: null);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.NotNull(log);
        Assert.Contains(SessionLogFlag.NoAgoraData, log!.Flags);
        // Without data we must not claim anyone failed to show up.
        Assert.DoesNotContain(SessionLogFlag.TutorNeverJoined, log.Flags);
        Assert.DoesNotContain(SessionLogFlag.ZeroOverlap, log.Flags);
        Assert.Null(log.Summary.SuggestedRefundPercentage);
        Assert.Equal(0, log.Summary.EventCount);
    }

    [Fact]
    public async Task TutorAloneInTheRoomYieldsZeroOverlapAndAFullRefundSuggestion()
    {
        await using var db = CreateContext();
        SeedSession(db);
        SeedParticipants(db, ScheduledStart, studentJoinsAt: null);

        AddJoin(db, "101", TutorUserId, ScheduledStart);
        AddLeave(db, "101", TutorUserId, ScheduledEnd, reason: 1, duration: 3600);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.NotNull(log);
        Assert.Equal(0, log!.Summary.OverlapSeconds);
        Assert.Contains(SessionLogFlag.ZeroOverlap, log.Flags);
        Assert.Contains(SessionLogFlag.StudentNeverJoined, log.Flags);
        Assert.Equal(100, log.Summary.SuggestedRefundPercentage);
    }

    [Fact]
    public async Task ParentStandsInForAStudentWithoutTheirOwnAccount()
    {
        await using var db = CreateContext();
        SeedSession(db, studentLinkedUserId: null);
        db.SessionParticipants.AddRange(
            Participant(TutorUserId, SessionParticipantRole.Tutor, ScheduledStart.AddSeconds(-5)),
            Participant(ParentUserId, SessionParticipantRole.Parent, ScheduledStart.AddSeconds(-3)));

        AddJoin(db, TutorUserId, TutorUserId, ScheduledStart);
        AddJoin(db, ParentUserId, ParentUserId, ScheduledStart);
        AddLeave(db, ParentUserId, ParentUserId, ScheduledEnd, reason: 1, duration: 3600);
        AddLeave(db, TutorUserId, TutorUserId, ScheduledEnd, reason: 1, duration: 3600);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.NotNull(log);
        Assert.Equal(3600, log!.Summary.OverlapSeconds);
        Assert.DoesNotContain(SessionLogFlag.ZeroOverlap, log.Flags);
    }

    [Fact]
    public async Task ParentDoesNotStandInWhenStudentHasALinkedAccount()
    {
        await using var db = CreateContext();
        SeedSession(db);

        AddJoin(db, TutorUserId, TutorUserId, ScheduledStart);
        AddJoin(db, ParentUserId, ParentUserId, ScheduledStart);
        AddLeave(
            db,
            ParentUserId,
            ParentUserId,
            ScheduledEnd,
            reason: AgoraLeaveReason.Quit,
            duration: 3600);
        AddLeave(
            db,
            TutorUserId,
            TutorUserId,
            ScheduledEnd,
            reason: AgoraLeaveReason.Quit,
            duration: 3600);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.Equal(3600, log!.Summary.TutorSeconds);
        Assert.Equal(0, log.Summary.StudentSeconds);
        Assert.Equal(0, log.Summary.OverlapSeconds);
        Assert.Equal(100, log.Summary.SuggestedRefundPercentage);
        Assert.Contains(SessionLogFlag.StudentNeverJoined, log.Flags);
    }

    [Fact]
    public async Task TokenErrorDisconnectIsFlaggedAsOurFault()
    {
        await using var db = CreateContext();
        SeedSession(db);
        SeedParticipants(db, ScheduledStart, studentJoinsAt: null);

        AddJoin(db, "101", TutorUserId, ScheduledStart);
        AddLeave(db, "101", TutorUserId, ScheduledStart.AddMinutes(5), reason: 12, duration: 300);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.Contains(SessionLogFlag.TokenError, log!.Flags);
    }

    [Fact]
    public async Task CheckedInSessionWithNoSharedTimeIsFlaggedAsContradictory()
    {
        await using var db = CreateContext();
        SeedSession(db, checkInTime: ScheduledStart.AddMinutes(1));
        SeedParticipants(db, ScheduledStart, studentJoinsAt: null);

        AddJoin(db, "101", TutorUserId, ScheduledStart);
        AddLeave(db, "101", TutorUserId, ScheduledEnd, reason: 1, duration: 3600);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.Contains(SessionLogFlag.CheckInMismatch, log!.Flags);
    }

    [Fact]
    public async Task LeaveDurationBeforeLaterRejoinUsesTheInferredFirstArrivalForIdentity()
    {
        await using var db = CreateContext();
        var admittedAt = new DateTime(2026, 7, 23, 19, 36, 26, DateTimeKind.Utc);
        var leaveAt = new DateTime(2026, 7, 23, 19, 55, 44, DateTimeKind.Utc);
        var rejoinAt = new DateTime(2026, 7, 23, 19, 55, 52, DateTimeKind.Utc);

        SeedSession(
            db,
            status: ClassSessionStatus.Completed,
            checkOutTime: rejoinAt.AddMinutes(1));
        db.SessionParticipants.Add(
            Participant(TutorUserId, SessionParticipantRole.Tutor, admittedAt));

        // Exact production sequence: the first join notification is missing, then the same uid
        // leaves with a duration and rejoins eight seconds later.
        AddLeave(
            db,
            "1000000003",
            account: null,
            leaveAt,
            reason: AgoraLeaveReason.Quit,
            duration: 1156,
            clientSequence: 10);
        AddJoin(
            db,
            "1000000003",
            account: null,
            rejoinAt,
            clientSequence: 11);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        var tutor = Assert.Single(log!.Participants, p => p.Role == SessionParticipantRole.Tutor);
        Assert.Equal("1000000003", tutor.AgoraUid);
        Assert.Equal(new DateTime(2026, 7, 23, 19, 36, 28, DateTimeKind.Utc), tutor.FirstJoinAt);
        Assert.Equal(2, tutor.JoinCount);
        Assert.Equal(2, tutor.Intervals.Count);
        Assert.Contains(SessionLogFlag.IdentityUncertain, log.Flags);
        Assert.False(log.Summary.IsEvidenceConclusive);
        Assert.Null(log.Summary.SuggestedRefundPercentage);
        Assert.DoesNotContain(SessionLogFlag.StudentNeverJoined, log.Flags);
        Assert.DoesNotContain(SessionLogFlag.ZeroOverlap, log.Flags);
    }

    [Fact]
    public async Task AgoraAccountBindsNumericUidExactlyAndMetadataIsExposed()
    {
        await using var db = CreateContext();
        SeedSession(db);

        const long joinSequence = 1_625_051_035_369;
        AddJoin(
            db,
            "2846271",
            TutorUserId,
            ScheduledStart,
            clientSequence: joinSequence,
            clientType: 42);
        AddLeave(
            db,
            "2846271",
            TutorUserId,
            ScheduledEnd,
            reason: AgoraLeaveReason.Quit,
            duration: 3600,
            clientSequence: joinSequence + 1,
            clientType: 42);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        var tutor = Assert.Single(log!.Participants, p => p.Role == SessionParticipantRole.Tutor);
        Assert.Equal(SessionLogIdentityConfidence.Exact, tutor.IdentityConfidence);
        Assert.DoesNotContain(SessionLogFlag.IdentityUncertain, log.Flags);

        var join = Assert.Single(log.Timeline, e => AgoraChannelEventType.IsJoin(e.EventType));
        Assert.Equal(TutorUserId, join.AgoraAccount);
        Assert.Equal(joinSequence, join.ClientSequence);
        Assert.Equal(42, join.ClientType);
        Assert.Equal(TutorUserId, join.AppUserId);
    }

    [Fact]
    public async Task ConflictingAccountsOnOneUidRemainUnmatchedAndInconclusive()
    {
        await using var db = CreateContext();
        SeedSession(db);

        AddJoin(db, "2846271", TutorUserId, ScheduledStart);
        AddLeave(
            db,
            "2846271",
            account: "unexpected_account",
            ScheduledEnd,
            reason: AgoraLeaveReason.Quit,
            duration: 3600);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.Contains(SessionLogFlag.InsufficientEvidence, log!.Flags);
        Assert.Contains(log.Participants, participant =>
            participant.AgoraUid == "2846271"
            && participant.Role == SessionParticipantRole.Unknown
            && participant.IdentityConfidence == SessionLogIdentityConfidence.Unmatched);
        Assert.False(log.Summary.IsEvidenceConclusive);
        Assert.Null(log.Summary.SuggestedRefundPercentage);
        Assert.DoesNotContain(SessionLogFlag.TutorNeverJoined, log.Flags);
    }

    [Fact]
    public async Task ReportedUnknownAccountCannotBeOverriddenByMatchingUidText()
    {
        await using var db = CreateContext();
        SeedSession(db);

        AddJoin(db, TutorUserId, account: "unexpected_account", ScheduledStart);
        AddLeave(
            db,
            TutorUserId,
            account: "unexpected_account",
            ScheduledEnd,
            reason: AgoraLeaveReason.Quit,
            duration: 3600);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.Contains(log!.Participants, participant =>
            participant.AgoraUid == TutorUserId
            && participant.Role == SessionParticipantRole.Unknown
            && participant.IdentityConfidence == SessionLogIdentityConfidence.Unmatched);
        Assert.False(log.Summary.IsEvidenceConclusive);
        Assert.Null(log.Summary.SuggestedRefundPercentage);
    }

    [Fact]
    public async Task BroadcasterEventsFromV4AreCountedAsJoinAndLeave()
    {
        await using var db = CreateContext();
        SeedSession(db);

        AddJoin(
            db,
            "101",
            TutorUserId,
            ScheduledStart,
            eventType: AgoraChannelEventType.BroadcasterJoin);
        AddJoin(
            db,
            "202",
            StudentUserId,
            ScheduledStart,
            eventType: AgoraChannelEventType.BroadcasterJoin);
        AddLeave(
            db,
            "101",
            TutorUserId,
            ScheduledEnd,
            reason: AgoraLeaveReason.Quit,
            duration: 3600,
            eventType: AgoraChannelEventType.BroadcasterLeave);
        AddLeave(
            db,
            "202",
            StudentUserId,
            ScheduledEnd,
            reason: AgoraLeaveReason.Quit,
            duration: 3600,
            eventType: AgoraChannelEventType.BroadcasterLeave);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.Equal(3600, log!.Summary.OverlapSeconds);
        Assert.True(log.Summary.IsEvidenceConclusive);
        Assert.All(log.Timeline, e =>
            Assert.Contains(e.EventLabel, new[] { "Vào phòng", "Rời phòng" }));
    }

    [Fact]
    public async Task OngoingTwoPartyRoomUsesOneSnapshotAndKeepsOpenParticipantsPresent()
    {
        await using var db = CreateContext();
        var joinedAt = DateTime.UtcNow.AddHours(-2);
        SeedSession(
            db,
            checkInTime: joinedAt.AddSeconds(2),
            status: ClassSessionStatus.InProgress);
        db.SessionParticipants.AddRange(
            Participant(TutorUserId, SessionParticipantRole.Tutor, joinedAt.AddSeconds(-2)),
            Participant(StudentUserId, SessionParticipantRole.Student, joinedAt.AddSeconds(-1)));
        AddJoin(db, "101", TutorUserId, joinedAt, clientSequence: 1);
        AddJoin(db, "202", StudentUserId, joinedAt, clientSequence: 1);
        await db.SaveChangesAsync();

        var beforeSnapshot = DateTime.UtcNow;
        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.True(log!.Summary.IsOngoing);
        Assert.False(log.Summary.IsEvidenceConclusive);
        Assert.Null(log.Summary.SuggestedRefundPercentage);
        Assert.True(log.Summary.SnapshotAt >= beforeSnapshot);
        Assert.InRange(log.Summary.OverlapSeconds, 7195, 7210);
        Assert.Equal(1d, log.Summary.OverlapRatio);
        Assert.DoesNotContain(SessionLogFlag.UnclosedInterval, log.Flags);
        Assert.DoesNotContain(SessionLogFlag.TutorNeverJoined, log.Flags);
        Assert.DoesNotContain(SessionLogFlag.StudentNeverJoined, log.Flags);

        var active = log.Participants
            .Where(p => p.Role is SessionParticipantRole.Tutor or SessionParticipantRole.Student)
            .ToList();
        Assert.Equal(2, active.Count);
        Assert.All(active, participant =>
        {
            Assert.True(participant.IsCurrentlyPresent);
            Assert.Null(participant.LastLeaveAt);
            Assert.Equal(log.Summary.SnapshotAt, participant.Intervals[^1].End);
        });
    }

    [Fact]
    public async Task RecorderUidAndClientTypeAreVisibleButExcludedFromAttendance()
    {
        await using var db = CreateContext();
        SeedSession(db);

        // The configured recorder id may arrive as account while uid is Agora's numeric mapping.
        AddJoin(db, "900001", account: "424242", ScheduledStart);
        AddLeave(
            db,
            "900001",
            account: "424242",
            ScheduledEnd,
            reason: AgoraLeaveReason.Quit,
            duration: 3600);
        AddJoin(db, "515151", account: null, ScheduledStart, clientType: 10);
        AddLeave(
            db,
            "515151",
            account: null,
            ScheduledEnd,
            reason: AgoraLeaveReason.Quit,
            duration: 3600,
            clientType: 10);
        await db.SaveChangesAsync();

        var log = await CreateService(db, recorderUid: 424242).GetSessionLogAsync(ClassSessionId);

        var recorders = log!.Participants
            .Where(p => p.Role == SessionParticipantRole.Recorder)
            .ToList();
        Assert.Equal(2, recorders.Count);
        Assert.All(recorders, recorder =>
        {
            Assert.Equal("Máy ghi hình Agora", recorder.DisplayName);
            Assert.Equal(3600, recorder.TotalSeconds);
        });
        Assert.Equal(0, log.Summary.TutorSeconds);
        Assert.Equal(0, log.Summary.StudentSeconds);
        Assert.False(log.Summary.IsEvidenceConclusive);
        Assert.Null(log.Summary.SuggestedRefundPercentage);
        Assert.Contains(SessionLogFlag.RecorderPresent, log.Flags);
        Assert.DoesNotContain(SessionLogFlag.TutorNeverJoined, log.Flags);
        Assert.DoesNotContain(SessionLogFlag.StudentNeverJoined, log.Flags);
        Assert.All(log.Timeline, e => Assert.Equal(SessionParticipantRole.Recorder, e.Role));
    }

    [Fact]
    public async Task UnmatchedHumanTrafficCannotProduceNoShowFlagsOrRefundGuidance()
    {
        await using var db = CreateContext();
        SeedSession(db);
        AddJoin(db, "777777", account: null, ScheduledStart);
        AddLeave(
            db,
            "777777",
            account: null,
            ScheduledEnd,
            reason: AgoraLeaveReason.Quit,
            duration: 3600);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.Contains(log!.Participants, p =>
            p.Role == SessionParticipantRole.Unknown && p.AgoraUid == "777777");
        Assert.False(log.Summary.IsEvidenceConclusive);
        Assert.Null(log.Summary.SuggestedRefundPercentage);
        Assert.DoesNotContain(SessionLogFlag.TutorNeverJoined, log.Flags);
        Assert.DoesNotContain(SessionLogFlag.StudentNeverJoined, log.Flags);
        Assert.DoesNotContain(SessionLogFlag.ZeroOverlap, log.Flags);
    }

    [Fact]
    public async Task ParticipantEventWithoutUidMakesOtherwiseResolvedEvidenceInconclusive()
    {
        await using var db = CreateContext();
        SeedSession(db);
        AddJoin(db, "101", TutorUserId, ScheduledStart);
        AddLeave(
            db,
            "101",
            TutorUserId,
            ScheduledEnd,
            reason: AgoraLeaveReason.Quit,
            duration: 3600);
        AddChannelEvent(
            db,
            AgoraChannelEventType.UserJoin,
            ScheduledStart.AddMinutes(1),
            uid: null,
            account: StudentUserId);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.Contains(SessionLogFlag.InsufficientEvidence, log!.Flags);
        Assert.False(log.Summary.IsEvidenceConclusive);
        Assert.Null(log.Summary.SuggestedRefundPercentage);
        Assert.DoesNotContain(SessionLogFlag.StudentNeverJoined, log.Flags);
        // The row remains visible in the raw timeline even though it cannot form a uid interval.
        Assert.Equal(3, log.Summary.EventCount);
        Assert.DoesNotContain(SessionLogFlag.NoAgoraData, log.Flags);
    }

    [Fact]
    public async Task ClientSequenceOrdersPerUserStateTransitionsWhenAvailable()
    {
        await using var db = CreateContext();
        SeedSession(db);

        AddJoin(db, "101", TutorUserId, ScheduledStart, clientSequence: 1);
        AddLeave(
            db,
            "101",
            TutorUserId,
            ScheduledStart.AddMinutes(20),
            reason: AgoraLeaveReason.Quit,
            duration: 0,
            clientSequence: 2);
        // Event timestamps overlap, but clientSeq says this is a later rejoin.
        AddJoin(db, "101", TutorUserId, ScheduledStart.AddMinutes(10), clientSequence: 3);
        AddLeave(
            db,
            "101",
            TutorUserId,
            ScheduledStart.AddMinutes(30),
            reason: AgoraLeaveReason.Quit,
            duration: 0,
            clientSequence: 4);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        var tutor = Assert.Single(log!.Participants, p => p.Role == SessionParticipantRole.Tutor);
        Assert.Equal(1800, tutor.TotalSeconds);
        Assert.Equal(2, tutor.JoinCount);
    }

    [Fact]
    public async Task SessionsPredatingTheRegistryStillResolveExactMatches()
    {
        await using var db = CreateContext();
        SeedSession(db);
        // No session_participants rows at all.
        AddJoin(db, TutorUserId, TutorUserId, ScheduledStart);
        AddLeave(db, TutorUserId, TutorUserId, ScheduledEnd, reason: 1, duration: 3600);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.NotNull(log);
        Assert.Contains(SessionLogFlag.NoParticipantRegistry, log!.Flags);
        var tutor = Assert.Single(log.Participants, p => p.Role == SessionParticipantRole.Tutor);
        Assert.Equal(SessionLogIdentityConfidence.Exact, tutor.IdentityConfidence);
    }

    [Fact]
    public async Task MissingClassSessionReturnsNull()
    {
        await using var db = CreateContext();
        Assert.Null(await CreateService(db).GetSessionLogAsync(999999));
    }

    [Fact]
    public async Task RecordAdmissionKeepsTheFirstArrivalAndCountsRenewals()
    {
        await using var db = CreateContext();
        SeedSession(db);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await service.RecordAdmissionAsync(ClassSessionId, TutorUserId, SessionParticipantRole.Tutor);
        var first = await db.SessionParticipants.SingleAsync();
        var firstAdmittedAt = first.FirstAdmittedAt;

        await service.RecordAdmissionAsync(ClassSessionId, TutorUserId, SessionParticipantRole.Tutor);

        var row = await db.SessionParticipants.SingleAsync();
        Assert.Equal(firstAdmittedAt, row.FirstAdmittedAt);
        Assert.Equal(2, row.AdmissionCount);
    }

    // ── Lobby evidence ────────────────────────────────────────────────────────

    [Fact]
    public async Task LobbyJoinRefreshAndDisconnectProduceOneAuditableVisit()
    {
        await using var db = CreateContext();
        SeedSession(db, status: ClassSessionStatus.Scheduled);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await service.RecordLobbyJoinAsync(
            ClassSessionId,
            TutorUserId,
            SessionParticipantRole.Tutor,
            "connection-a");
        await service.RecordLobbyHeartbeatAsync(ClassSessionId, TutorUserId, "connection-a");
        await service.CloseLobbyVisitAsync(
            ClassSessionId,
            TutorUserId,
            "connection-a",
            SessionLobbyVisitCloseReason.Disconnect);

        var visit = await db.SessionLobbyVisits.SingleAsync();
        Assert.Equal(2, visit.BeatCount);
        Assert.Equal(SessionLobbyVisitCloseReason.Disconnect, visit.ClosedReason);
        Assert.NotNull(visit.LeftAt);
        Assert.True(visit.LastSeenAt >= visit.EnteredAt);
    }

    [Fact]
    public async Task LobbyLogShowsWhichSideWaitedAndWhetherTheyReachedRoomAdmission()
    {
        await using var db = CreateContext();
        SeedSession(db);
        db.SessionLobbyVisits.Add(new SessionLobbyVisit
        {
            ClassSessionId = ClassSessionId,
            AppUserId = TutorUserId,
            Role = SessionParticipantRole.Tutor,
            ConnectionId = "connection-a",
            EnteredAt = ScheduledStart,
            LastSeenAt = ScheduledStart.AddMinutes(10),
            BeatCount = 61,
            LeftAt = ScheduledStart.AddMinutes(10),
            ClosedReason = SessionLobbyVisitCloseReason.Leave
        });
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.NotNull(log);
        Assert.True(log!.Lobby.HasAnyRecord);
        Assert.True(log.Lobby.TutorRecorded);
        Assert.False(log.Lobby.StudentSideRecorded);
        Assert.False(log.Lobby.BothSidesRecorded);

        var tutor = Assert.Single(log.Lobby.Participants);
        Assert.Equal("Gia sư", tutor.DisplayName);
        Assert.Equal(600, tutor.TotalSeconds);
        Assert.Equal(61, tutor.BeatCount);
        Assert.False(tutor.WasAdmittedToRoom);
        Assert.Equal("Rời lobby/chuyển sang phòng học", Assert.Single(tutor.Visits).ClosedReasonLabel);
    }

    [Fact]
    public async Task MissingLobbyRowsAreReportedAsMissingDataRatherThanAbsence()
    {
        await using var db = CreateContext();
        SeedSession(db);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.NotNull(log);
        Assert.False(log!.Lobby.HasAnyRecord);
        Assert.False(log.Lobby.TutorRecorded);
        Assert.False(log.Lobby.StudentSideRecorded);
        Assert.Empty(log.Lobby.Participants);
    }

    [Fact]
    public async Task ParentLobbyVisitCountsAsStudentSideOnlyWhenStudentHasNoLinkedLogin()
    {
        await using var db = CreateContext();
        SeedSession(db, studentLinkedUserId: null);
        db.SessionLobbyVisits.Add(new SessionLobbyVisit
        {
            ClassSessionId = ClassSessionId,
            AppUserId = ParentUserId,
            Role = SessionParticipantRole.Parent,
            ConnectionId = "connection-parent",
            EnteredAt = ScheduledStart,
            LastSeenAt = ScheduledStart.AddMinutes(1),
            BeatCount = 7,
            LeftAt = ScheduledStart.AddMinutes(1),
            ClosedReason = SessionLobbyVisitCloseReason.Leave
        });
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.NotNull(log);
        Assert.True(log!.Lobby.StudentSideRecorded);
        Assert.Equal(SessionParticipantRole.Parent, Assert.Single(log.Lobby.Participants).Role);
    }

    // ── Networks and devices ──────────────────────────────────────────────────

    [Fact]
    public async Task AdmissionKeepsOneRowPerDeviceAndCountsRepeatArrivals()
    {
        await using var db = CreateContext();
        SeedSession(db);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var laptop = new SessionAdmissionContext("203.0.113.7", "device-a", "Chrome", "Mozilla/5.0");

        // Token renewal re-enters admission every couple of minutes and must not multiply rows.
        await service.RecordAdmissionAsync(ClassSessionId, TutorUserId, SessionParticipantRole.Tutor, laptop);
        await service.RecordAdmissionAsync(ClassSessionId, TutorUserId, SessionParticipantRole.Tutor, laptop);

        var single = await db.SessionParticipantDevices.SingleAsync();
        Assert.Equal(2, single.AdmissionCount);
        Assert.Equal("203.0.113.7", single.IpAddress);

        await service.RecordAdmissionAsync(
            ClassSessionId,
            TutorUserId,
            SessionParticipantRole.Tutor,
            new SessionAdmissionContext("198.51.100.4", "device-b", "Safari", "Mozilla/5.0"));

        Assert.Equal(2, await db.SessionParticipantDevices.CountAsync());
    }

    [Fact]
    public async Task OneAccountFromTwoNetworksAndDevicesIsFlagged()
    {
        await using var db = CreateContext();
        SeedSession(db);
        SeedParticipants(db, ScheduledStart, ScheduledStart);
        AddDevice(db, TutorUserId, SessionParticipantRole.Tutor, "203.0.113.7", "device-a", ScheduledStart);
        AddDevice(db, TutorUserId, SessionParticipantRole.Tutor, "198.51.100.4", "device-b", ScheduledStart.AddMinutes(5));
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.NotNull(log);
        Assert.Contains(SessionLogFlag.MultipleNetworks, log!.Flags);
        Assert.Contains(SessionLogFlag.MultipleDevices, log.Flags);
        Assert.Equal(2, log.Devices.Count);
        // Rows come back named from the booking, not as bare user ids.
        Assert.All(log.Devices, device => Assert.Equal("Gia sư", device.DisplayName));
    }

    [Fact]
    public async Task AddressesWeCouldNotCaptureDoNotCountAsASecondNetwork()
    {
        await using var db = CreateContext();
        SeedSession(db);
        SeedParticipants(db, ScheduledStart, ScheduledStart);
        AddDevice(db, TutorUserId, SessionParticipantRole.Tutor, "203.0.113.7", "device-a", ScheduledStart);
        // A failed capture is stored as empty and must not look like a second place to log in from.
        AddDevice(db, TutorUserId, SessionParticipantRole.Tutor, "", "device-a", ScheduledStart.AddMinutes(5));
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.NotNull(log);
        Assert.DoesNotContain(SessionLogFlag.MultipleNetworks, log!.Flags);
        Assert.DoesNotContain(SessionLogFlag.MultipleDevices, log.Flags);
    }

    [Fact]
    public async Task SessionsPredatingTheDeviceRecordSaySoInsteadOfLookingClean()
    {
        await using var db = CreateContext();
        SeedSession(db);
        SeedParticipants(db, ScheduledStart, ScheduledStart);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.NotNull(log);
        Assert.Contains(SessionLogFlag.NoDeviceRecord, log!.Flags);
        Assert.Empty(log.Devices);
    }

    // ── Heartbeat chain ───────────────────────────────────────────────────────

    [Fact]
    public async Task ConsecutiveBeatsExtendOneRunAndASilenceStartsANewOne()
    {
        await using var db = CreateContext();
        SeedSession(db, status: ClassSessionStatus.InProgress);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await service.RecordHeartbeatAsync(ClassSessionId, TutorUserId, SessionParticipantRole.Tutor, null);
        await service.RecordHeartbeatAsync(ClassSessionId, TutorUserId, SessionParticipantRole.Tutor, null);

        var run = await db.SessionPresenceIntervals.SingleAsync();
        Assert.Equal(2, run.BeatCount);
        Assert.Null(run.ClosedReason);

        // Rewind the run past the gap window: the next beat cannot honestly continue it.
        run.LastBeatAt = run.LastBeatAt.AddMinutes(-10);
        run.StartedAt = run.StartedAt.AddMinutes(-10);
        await db.SaveChangesAsync();

        await service.RecordHeartbeatAsync(ClassSessionId, TutorUserId, SessionParticipantRole.Tutor, null);

        var runs = await db.SessionPresenceIntervals.OrderBy(i => i.StartedAt).ToListAsync();
        Assert.Equal(2, runs.Count);
        Assert.Equal(PresenceIntervalCloseReason.Gap, runs[0].ClosedReason);
        Assert.Null(runs[1].ClosedReason);
        Assert.Equal(1, runs[1].BeatCount);
    }

    [Fact]
    public async Task LeavingClosesTheRunAsIntentionalRatherThanAsASilence()
    {
        await using var db = CreateContext();
        SeedSession(db, status: ClassSessionStatus.InProgress);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await service.RecordHeartbeatAsync(ClassSessionId, TutorUserId, SessionParticipantRole.Tutor, null);
        await service.CloseHeartbeatAsync(ClassSessionId, TutorUserId);

        var run = await db.SessionPresenceIntervals.SingleAsync();
        Assert.Equal(PresenceIntervalCloseReason.Leave, run.ClosedReason);
    }

    [Fact]
    public async Task RacingFirstBeatsAreReportedAsOneRunWhoseLeaveSurvives()
    {
        await using var db = CreateContext();
        SeedSession(db);
        SeedParticipants(db, ScheduledStart, ScheduledStart);
        // Two beats fired concurrently at room entry: each opened its own row before the other's
        // insert was visible. The stray one-beat twin stays open forever; the real run carried the
        // whole lesson and was closed by a deliberate leave.
        AddBeatRun(
            db, StudentUserId, SessionParticipantRole.Student,
            ScheduledStart, ScheduledStart, beatCount: 1, closedReason: null);
        AddBeatRun(
            db, StudentUserId, SessionParticipantRole.Student,
            ScheduledStart.AddMilliseconds(8), ScheduledStart.AddMinutes(40),
            beatCount: 120, closedReason: PresenceIntervalCloseReason.Leave);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.NotNull(log);
        var student = Assert.Single(log!.Heartbeats, h => h.Role == SessionParticipantRole.Student);
        var run = Assert.Single(student.Runs);
        // The twin that kept beating longer holds the truth about how the run ended.
        Assert.Equal(PresenceIntervalCloseReason.Leave, run.ClosedReason);
        Assert.Equal(121, student.BeatCount);
        Assert.Equal(0, student.GapCount);
    }

    [Fact]
    public async Task LeaveThenRejoinStaysTwoRunsSoTheStoryIsNotErased()
    {
        await using var db = CreateContext();
        SeedSession(db);
        SeedParticipants(db, ScheduledStart, ScheduledStart);
        // A real exit and a return half a minute later — exactly what the chain must preserve.
        AddBeatRun(
            db, StudentUserId, SessionParticipantRole.Student,
            ScheduledStart, ScheduledStart.AddMinutes(5),
            beatCount: 15, closedReason: PresenceIntervalCloseReason.Leave);
        AddBeatRun(
            db, StudentUserId, SessionParticipantRole.Student,
            ScheduledStart.AddMinutes(5).AddSeconds(31), ScheduledStart.AddMinutes(40),
            beatCount: 100, closedReason: PresenceIntervalCloseReason.Leave);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.NotNull(log);
        var student = Assert.Single(log!.Heartbeats, h => h.Role == SessionParticipantRole.Student);
        Assert.Equal(2, student.Runs.Count);
        Assert.All(student.Runs, r => Assert.Equal(PresenceIntervalCloseReason.Leave, r.ClosedReason));
    }

    [Fact]
    public async Task ActivityIsCountedOnlyAsAnUnattendedRoomWhenNothingIsPublishing()
    {
        await using var db = CreateContext();
        SeedSession(db, status: ClassSessionStatus.InProgress);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        // Teaching with the camera off is ordinary, so a live microphone is never "unattended".
        await service.RecordHeartbeatAsync(
            ClassSessionId, TutorUserId, SessionParticipantRole.Tutor,
            new LiveSessionActivityReport { MicOn = true, CameraOn = false, Idle = true });
        await service.RecordHeartbeatAsync(
            ClassSessionId, TutorUserId, SessionParticipantRole.Tutor,
            new LiveSessionActivityReport { MicOn = false, CameraOn = false, Idle = true });

        var run = await db.SessionPresenceIntervals.SingleAsync();
        Assert.Equal(2, run.ReportedBeats);
        Assert.Equal(1, run.MicOnBeats);
        Assert.Equal(1, run.IdleBeats);
    }

    [Fact]
    public async Task MostlyIdleBeatsFlagARoomLeftOpen()
    {
        await using var db = CreateContext();
        SeedSession(db);
        SeedParticipants(db, ScheduledStart, ScheduledStart);
        AddJoin(db, "101", TutorUserId, ScheduledStart);
        AddLeave(db, "101", TutorUserId, ScheduledEnd, reason: 1, duration: 3600);
        AddBeatRun(
            db, TutorUserId, SessionParticipantRole.Tutor, ScheduledStart, ScheduledEnd,
            beatCount: 10, reportedBeats: 10, idleBeats: 9);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.NotNull(log);
        Assert.Contains(SessionLogFlag.IdlePresence, log!.Flags);
        var tutorBeats = Assert.Single(log.Heartbeats, h => h.Role == SessionParticipantRole.Tutor);
        Assert.Equal(0.9, tutorBeats.IdleRatio);
        Assert.Equal(3600, tutorBeats.TotalSeconds);
    }

    [Fact]
    public async Task AClientThatReportsNoActivityIsUnknownRatherThanIdle()
    {
        await using var db = CreateContext();
        SeedSession(db);
        SeedParticipants(db, ScheduledStart, ScheduledStart);
        AddBeatRun(db, TutorUserId, SessionParticipantRole.Tutor, ScheduledStart, ScheduledEnd);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.NotNull(log);
        Assert.Contains(SessionLogFlag.NoActivityReports, log!.Flags);
        Assert.DoesNotContain(SessionLogFlag.IdlePresence, log.Flags);
        Assert.Null(Assert.Single(log.Heartbeats).IdleRatio);
    }

    [Fact]
    public async Task TooFewReportedBeatsCannotClaimAnUnattendedRoom()
    {
        await using var db = CreateContext();
        SeedSession(db);
        SeedParticipants(db, ScheduledStart, ScheduledStart);
        // One idle beat is a mid-lesson reload, not an absent tutor.
        AddBeatRun(
            db, TutorUserId, SessionParticipantRole.Tutor, ScheduledStart, ScheduledEnd,
            beatCount: 1, reportedBeats: 1, idleBeats: 1);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.NotNull(log);
        Assert.DoesNotContain(SessionLogFlag.IdlePresence, log!.Flags);
    }

    [Fact]
    public async Task BeatsWithoutAgoraDataBlockANoShowConclusion()
    {
        await using var db = CreateContext();
        SeedSession(db);
        SeedParticipants(db, ScheduledStart, ScheduledStart);
        // Both sides beat for the full hour, but Agora sent nothing at all.
        AddBeatRun(db, TutorUserId, SessionParticipantRole.Tutor, ScheduledStart, ScheduledEnd);
        AddBeatRun(db, StudentUserId, SessionParticipantRole.Student, ScheduledStart.AddMinutes(10), ScheduledEnd);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.NotNull(log);
        Assert.Contains(SessionLogFlag.PresenceWithoutAgora, log!.Flags);
        Assert.False(log.Summary.IsEvidenceConclusive);
        Assert.DoesNotContain(SessionLogFlag.TutorNeverJoined, log.Flags);
        Assert.DoesNotContain(SessionLogFlag.StudentNeverJoined, log.Flags);
        Assert.Null(log.Summary.SuggestedRefundPercentage);

        // The heartbeat figures still stand on their own — 12:10 to 13:00 shared.
        Assert.Equal(3000, log.Summary.HeartbeatOverlapSeconds);
        Assert.Equal(3600, log.Summary.TutorHeartbeatSeconds);
        Assert.Equal(0, log.Summary.OverlapSeconds);
    }

    // ── Punctuality ───────────────────────────────────────────────────────────

    [Fact]
    public async Task LatenessAndEarlyDepartureAreMeasuredPastTheGracePeriod()
    {
        await using var db = CreateContext();
        SeedSession(db);
        SeedParticipants(db, ScheduledStart.AddMinutes(10), ScheduledStart);
        AddJoin(db, "101", TutorUserId, ScheduledStart.AddMinutes(10));
        AddLeave(db, "101", TutorUserId, ScheduledEnd.AddMinutes(-10), reason: 1, duration: 2400);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.NotNull(log);
        // Ten minutes each way, less the five-minute grace.
        Assert.Equal(300, log!.Summary.TutorLateSeconds);
        Assert.Equal(300, log.Summary.TutorEarlyLeaveSeconds);
        Assert.Equal(SessionLogPunctualitySource.Agora, log.Summary.PunctualitySource);
    }

    [Fact]
    public async Task ArrivingWithinTheGracePeriodIsNotLate()
    {
        await using var db = CreateContext();
        SeedSession(db);
        SeedParticipants(db, ScheduledStart.AddMinutes(2), ScheduledStart);
        AddJoin(db, "101", TutorUserId, ScheduledStart.AddMinutes(2));
        AddLeave(db, "101", TutorUserId, ScheduledEnd, reason: 1, duration: 3480);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.NotNull(log);
        Assert.Equal(0, log!.Summary.TutorLateSeconds);
        Assert.Equal(0, log.Summary.TutorEarlyLeaveSeconds);
    }

    [Fact]
    public async Task PunctualityFallsBackToTheBeatsAndSaysSo()
    {
        await using var db = CreateContext();
        SeedSession(db);
        SeedParticipants(db, ScheduledStart.AddMinutes(20), ScheduledStart);
        AddBeatRun(
            db, TutorUserId, SessionParticipantRole.Tutor,
            ScheduledStart.AddMinutes(20), ScheduledEnd);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.NotNull(log);
        Assert.Equal(SessionLogPunctualitySource.Heartbeat, log!.Summary.PunctualitySource);
        Assert.Equal(900, log.Summary.TutorLateSeconds);
    }

    [Fact]
    public async Task ATutorWhoNeverArrivedHasNoLatenessNumber()
    {
        await using var db = CreateContext();
        SeedSession(db);
        SeedParticipants(db, ScheduledStart, ScheduledStart);
        AddJoin(db, "202", StudentUserId, ScheduledStart);
        AddLeave(db, "202", StudentUserId, ScheduledEnd, reason: 1, duration: 3600);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);

        Assert.NotNull(log);
        Assert.Null(log!.Summary.TutorLateSeconds);
        Assert.Null(log.Summary.PunctualitySource);
        Assert.Contains(SessionLogFlag.TutorNeverJoined, log.Flags);
    }

    // ── Tutor reliability ─────────────────────────────────────────────────────

    [Fact]
    public async Task ReliabilityRatesCountOnlyLessonsWhoseEvidenceCouldBeJudged()
    {
        await using var db = CreateContext();
        SeedSession(db);
        SeedParticipants(db, ScheduledStart.AddMinutes(10), ScheduledStart);
        AddJoin(db, "101", TutorUserId, ScheduledStart.AddMinutes(10));
        AddLeave(db, "101", TutorUserId, ScheduledEnd, reason: 1, duration: 3000);

        // A second lesson with no evidence at all from either source.
        var secondStart = ScheduledStart.AddDays(1);
        AddSession(db, ClassSessionId + 1, secondStart);
        await db.SaveChangesAsync();

        var report = await CreateService(db).GetTutorReliabilityAsync(
            TutorUserId,
            ScheduledStart.AddDays(-1),
            ScheduledStart.AddDays(7));

        Assert.Equal(2, report.SessionsInRange);
        Assert.Equal(1, report.SessionsMeasured);
        Assert.Equal(1, report.SessionsWithoutEvidence);
        Assert.Equal(1, report.LateCount);
        Assert.Equal(1.0, report.LateRate);
        Assert.Equal(300, report.AverageLateSeconds);
        Assert.Equal(0, report.NoShowCount);

        var unmeasured = Assert.Single(report.Sessions, s => !s.IsMeasured);
        Assert.Equal(ClassSessionId + 1, unmeasured.ClassSessionId);
        Assert.Contains(SessionLogFlag.NoAgoraData, unmeasured.Flags);
    }

    [Fact]
    public async Task ReliabilityCountsAConclusiveAbsenceAsANoShow()
    {
        await using var db = CreateContext();
        SeedSession(db);
        SeedParticipants(db, ScheduledStart, ScheduledStart);
        // Only the student ever reached the channel, and the evidence is complete.
        AddJoin(db, "202", StudentUserId, ScheduledStart);
        AddLeave(db, "202", StudentUserId, ScheduledEnd, reason: 1, duration: 3600);
        await db.SaveChangesAsync();

        var report = await CreateService(db).GetTutorReliabilityAsync(
            TutorUserId,
            ScheduledStart.AddDays(-1),
            ScheduledStart.AddDays(1));

        Assert.Equal(1, report.SessionsMeasured);
        Assert.Equal(1, report.NoShowCount);
        Assert.Equal(1.0, report.NoShowRate);
        // A tutor who never arrived is a no-show, not additionally "late" and "left early".
        Assert.Equal(0, report.LateCount);
        Assert.Equal(0, report.EarlyLeaveCount);
        Assert.True(Assert.Single(report.Sessions).IsNoShow);
    }

    [Fact]
    public async Task ReliabilityDoesNotAccuseATutorOfLeavingEarlyOnAnIntervalWeClosedOurselves()
    {
        await using var db = CreateContext();
        SeedSession(db);
        SeedParticipants(db, ScheduledStart, ScheduledStart);
        // A join whose leave never arrived: the log closes it at the last known event, which says
        // nothing about when the tutor actually left.
        AddJoin(db, "101", TutorUserId, ScheduledStart);
        AddJoin(db, "202", StudentUserId, ScheduledStart);
        AddLeave(db, "202", StudentUserId, ScheduledEnd.AddMinutes(-20), reason: 1, duration: 2400);
        await db.SaveChangesAsync();

        var log = await CreateService(db).GetSessionLogAsync(ClassSessionId);
        Assert.NotNull(log);
        Assert.Contains(SessionLogFlag.UnclosedInterval, log!.Flags);
        Assert.True(log.Summary.TutorEarlyLeaveSeconds > 0);

        var report = await CreateService(db).GetTutorReliabilityAsync(
            TutorUserId,
            ScheduledStart.AddDays(-1),
            ScheduledStart.AddDays(1));

        Assert.Equal(0, report.EarlyLeaveCount);
        Assert.Null(report.AverageEarlyLeaveSeconds);
    }

    [Fact]
    public async Task ReliabilityIgnoresCancelledLessons()
    {
        await using var db = CreateContext();
        SeedSession(db);
        AddSession(db, ClassSessionId + 1, ScheduledStart.AddDays(1), ClassSessionStatus.Cancelled);
        await db.SaveChangesAsync();

        var report = await CreateService(db).GetTutorReliabilityAsync(
            TutorUserId,
            ScheduledStart.AddDays(-1),
            ScheduledStart.AddDays(7));

        Assert.Equal(1, report.SessionsInRange);
        Assert.DoesNotContain(report.Sessions, s => s.ClassSessionId == ClassSessionId + 1);
    }

    // ── Interval maths, exercised directly ────────────────────────────────────

    [Fact]
    public void MergeCollapsesOverlappingWindowsSoRejoinsAreNotDoubleCounted()
    {
        var merged = SessionLogService.Merge([
            new SessionLogInterval(ScheduledStart, ScheduledStart.AddMinutes(30)),
            new SessionLogInterval(ScheduledStart.AddMinutes(20), ScheduledStart.AddMinutes(40))
        ]);

        Assert.Single(merged);
        Assert.Equal(2400, SessionLogService.TotalSeconds(merged));
    }

    [Fact]
    public void IntersectReturnsOnlySharedWindows()
    {
        var left = SessionLogService.Merge([
            new SessionLogInterval(ScheduledStart, ScheduledStart.AddMinutes(10)),
            new SessionLogInterval(ScheduledStart.AddMinutes(20), ScheduledStart.AddMinutes(40))
        ]);
        var right = SessionLogService.Merge([
            new SessionLogInterval(ScheduledStart.AddMinutes(5), ScheduledStart.AddMinutes(25))
        ]);

        var shared = SessionLogService.Intersect(left, right);

        Assert.Equal(2, shared.Count);
        Assert.Equal(600, SessionLogService.TotalSeconds(shared));
    }

    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseInMemoryDatabase($"session-log-{Guid.NewGuid()}")
            .Options;
        return new TestDbContext(options);
    }

    private static SessionLogService CreateService(
        TestDbContext db,
        uint recorderUid = 999999,
        SessionEvidenceSettings? evidence = null)
        => new(
            db,
            NullLogger<SessionLogService>.Instance,
            Options.Create(new AgoraRecordingSettings { RecorderUid = recorderUid }),
            Options.Create(evidence ?? new SessionEvidenceSettings()));

    private static void SeedSession(
        TestDbContext db,
        string? studentLinkedUserId = StudentUserId,
        DateTime? checkInTime = null,
        string? status = ClassSessionStatus.Completed,
        DateTime? checkOutTime = null)
    {
        db.Studentprofiles.Add(new Studentprofile
        {
            Studentid = StudentProfileId,
            Linkeduserid = studentLinkedUserId,
            Fullname = "Học viên A"
        });

        db.Bookings.Add(new Booking
        {
            Bookingid = 1,
            Parentid = ParentUserId,
            Studentid = StudentProfileId
        });

        db.ClassSessions.Add(new ClassSession
        {
            Classsessionid = ClassSessionId,
            Bookingid = 1,
            Tutorid = TutorUserId,
            Scheduledstart = ScheduledStart,
            Scheduledend = ScheduledEnd,
            Checkintime = checkInTime,
            Checkouttime = checkOutTime,
            Status = status
        });
    }

    /// <summary>
    /// Admission happens moments before the client actually reaches the channel, so each expected
    /// join gets its own token request. Pass <c>studentJoinsAt: null</c> for a student who never
    /// opened the room — they never hit the join endpoint, so no admission row exists for them.
    /// </summary>
    private static void SeedParticipants(
        TestDbContext db,
        DateTime tutorJoinsAt,
        DateTime? studentJoinsAt)
    {
        db.SessionParticipants.Add(
            Participant(TutorUserId, SessionParticipantRole.Tutor, tutorJoinsAt.AddSeconds(-5)));

        if (studentJoinsAt.HasValue)
        {
            db.SessionParticipants.Add(
                Participant(StudentUserId, SessionParticipantRole.Student, studentJoinsAt.Value.AddSeconds(-4)));
        }
    }

    private static SessionParticipant Participant(string userId, string role, DateTime admittedAt) => new()
    {
        ClassSessionId = ClassSessionId,
        AppUserId = userId,
        Role = role,
        FirstAdmittedAt = admittedAt,
        LastAdmittedAt = admittedAt,
        AdmissionCount = 1
    };

    private static void AddJoin(
        TestDbContext db,
        string uid,
        string? account,
        DateTime at,
        short eventType = AgoraChannelEventType.UserJoin,
        long? clientSequence = null,
        int? clientType = null)
        => AddChannelEvent(
            db,
            eventType,
            at,
            uid,
            account: account,
            clientSequence: clientSequence,
            clientType: clientType);

    private static void AddLeave(
        TestDbContext db,
        string uid,
        string? account,
        DateTime at,
        int reason,
        int duration,
        short eventType = AgoraChannelEventType.UserLeave,
        long? clientSequence = null,
        int? clientType = null)
        => AddChannelEvent(
            db,
            eventType,
            at,
            uid,
            reason,
            duration,
            account,
            clientSequence,
            clientType);

    private static void AddChannelEvent(
        TestDbContext db,
        short eventType,
        DateTime at,
        string? uid,
        int? reason = null,
        int? duration = null,
        string? account = null,
        long? clientSequence = null,
        int? clientType = null,
        int classSessionId = ClassSessionId)
    {
        var fields = new List<string> { $"\"channelName\":\"{classSessionId}\"", $"\"ts\":{ToUnix(at)}" };
        if (uid != null)
        {
            // Numeric-looking uids are emitted as JSON numbers, matching what Agora sends.
            var isNumeric = long.TryParse(uid, out _);
            fields.Add(isNumeric ? $"\"uid\":{uid}" : $"\"uid\":\"{uid}\"");
            fields.Add("\"platform\":7");
        }
        if (!string.IsNullOrWhiteSpace(account))
            fields.Add($"\"account\":{JsonSerializer.Serialize(account)}");
        if (clientSequence.HasValue) fields.Add($"\"clientSeq\":{clientSequence.Value}");
        if (clientType.HasValue) fields.Add($"\"clientType\":{clientType.Value}");
        if (reason.HasValue) fields.Add($"\"reason\":{reason.Value}");
        if (duration.HasValue) fields.Add($"\"duration\":{duration.Value}");

        db.AgoraChannelEvents.Add(new AgoraChannelEvent
        {
            NoticeId = Guid.NewGuid().ToString("N"),
            ClassSessionId = classSessionId,
            EventType = eventType,
            EventAt = at,
            ReceivedAt = at.AddSeconds(1),
            Payload = "{" + string.Join(",", fields) + "}"
        });
    }

    /// <summary>A second lesson on the same booking, for the multi-session reliability report.</summary>
    private static void AddSession(
        TestDbContext db,
        int classSessionId,
        DateTime scheduledStart,
        string? status = ClassSessionStatus.Completed)
    {
        db.ClassSessions.Add(new ClassSession
        {
            Classsessionid = classSessionId,
            Bookingid = 1,
            Tutorid = TutorUserId,
            Scheduledstart = scheduledStart,
            Scheduledend = scheduledStart.AddHours(1),
            Status = status
        });
    }

    /// <summary>
    /// A run of heartbeats as the classroom client would have produced it. <paramref name="reportedBeats"/>
    /// left at 0 stands for a client too old to report activity at all.
    /// </summary>
    private static void AddBeatRun(
        TestDbContext db,
        string userId,
        string role,
        DateTime startedAt,
        DateTime lastBeatAt,
        int beatCount = 10,
        int reportedBeats = 0,
        int micOnBeats = 0,
        int cameraOnBeats = 0,
        int idleBeats = 0,
        string? closedReason = PresenceIntervalCloseReason.Leave,
        int classSessionId = ClassSessionId)
    {
        db.SessionPresenceIntervals.Add(new SessionPresenceInterval
        {
            ClassSessionId = classSessionId,
            AppUserId = userId,
            Role = role,
            StartedAt = startedAt,
            LastBeatAt = lastBeatAt,
            BeatCount = beatCount,
            ReportedBeats = reportedBeats,
            MicOnBeats = micOnBeats,
            CameraOnBeats = cameraOnBeats,
            IdleBeats = idleBeats,
            ClosedReason = closedReason
        });
    }

    private static void AddDevice(
        TestDbContext db,
        string userId,
        string role,
        string ipAddress,
        string deviceId,
        DateTime seenAt,
        int classSessionId = ClassSessionId)
    {
        db.SessionParticipantDevices.Add(new SessionParticipantDevice
        {
            ClassSessionId = classSessionId,
            AppUserId = userId,
            Role = role,
            IpAddress = ipAddress,
            DeviceId = deviceId,
            DeviceLabel = "Chrome trên Windows",
            UserAgent = "Mozilla/5.0",
            FirstSeenAt = seenAt,
            LastSeenAt = seenAt,
            AdmissionCount = 1
        });
    }

    private static long ToUnix(DateTime at) => new DateTimeOffset(at, TimeSpan.Zero).ToUnixTimeSeconds();

    private sealed class TestDbContext(DbContextOptions<AgoraDbContext> options) : AgoraDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<QuestionBank>().Ignore(question => question.Embedding);
            modelBuilder.Entity<TutoraKbChunk>().Ignore(chunk => chunk.Embedding);
        }
    }
}
