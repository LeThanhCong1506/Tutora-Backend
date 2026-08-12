using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Interfaces;

public interface IAppDbContext
{
    DatabaseFacade Database { get; }

    DbSet<AdminWalletTransfer> AdminWalletTransfers { get; }
    DbSet<BankAccount> BankAccounts { get; }
    DbSet<BankAccountAuditLog> BankAccountAuditLogs { get; }
    DbSet<Booking> Bookings { get; }
    DbSet<FraudLog> Fraudlogs { get; }
    DbSet<LoginHistory> Loginhistories { get; }
    DbSet<Chatchannel> Chatchannels { get; }
    DbSet<Chatmessage> Chatmessages { get; }
    DbSet<ChatSession> ChatSessions { get; }
    DbSet<ChatHistory> ChatHistories { get; }
    DbSet<StudentTopicSignal> StudentTopicSignals { get; }
    DbSet<AiMessageVote> AiMessageVotes { get; }
    DbSet<TutorSuggestionVote> TutorSuggestionVotes { get; }
    DbSet<Class> Classes { get; }
    DbSet<Dispute> Disputes { get; }
    DbSet<DisputeEvidence> DisputeEvidences { get; }
    DbSet<DisputeMessage> DisputeMessages { get; }
    DbSet<Feedback> Feedbacks { get; }
    DbSet<Handoversummary> Handoversummaries { get; }
    DbSet<Learningmaterial> Learningmaterials { get; }
    DbSet<ClassSession> ClassSessions { get; }
    DbSet<ClassSessionReport> ClassSessionReports { get; }
    DbSet<ClassSessionScheduleChange> ClassSessionScheduleChanges { get; }
    DbSet<ClassSessionRescheduleProposal> ClassSessionRescheduleProposals { get; }
    DbSet<SessionEngagementSample> SessionEngagementSamples { get; }
    DbSet<AgoraChannelEvent> AgoraChannelEvents { get; }
    DbSet<SessionParticipant> SessionParticipants { get; }
    DbSet<SessionParticipantDevice> SessionParticipantDevices { get; }
    DbSet<SessionPresenceInterval> SessionPresenceIntervals { get; }
    DbSet<SessionLobbyVisit> SessionLobbyVisits { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<QuestionBank> QuestionBanks { get; }
    DbSet<TutoraKbDocument> TutoraKbDocuments { get; }
    DbSet<TutoraKbChunk> TutoraKbChunks { get; }
    DbSet<QuestionVote> QuestionVotes { get; }
    DbSet<AiCreditTransaction> AiCreditTransactions { get; }
    DbSet<AiUsageMonthly> AiUsageMonthly { get; }
    DbSet<Profilesuspension> Profilesuspensions { get; }
    DbSet<Promotion> Promotions { get; }
    DbSet<Gradelevel> Gradelevels { get; }
    DbSet<Dayofweek> DaysOfWeek { get; }
    DbSet<Studentgrade> Studentgrades { get; }
    DbSet<Studentprofile> Studentprofiles { get; }
    DbSet<Subject> Subjects { get; }
    DbSet<Chapter> Chapters { get; }
    DbSet<QuestionType> QuestionTypes { get; }
    DbSet<PolicyDocument> PolicyDocuments { get; }

    DbSet<Systemconfig> Systemconfigs { get; }
    DbSet<AiCreditPackage> AiCreditPackages { get; }
    DbSet<Topuprequest> Topuprequests { get; }
    DbSet<Tutoravailability> Tutoravailabilities { get; }
    DbSet<Tutorcertificate> Tutorcertificates { get; }
    DbSet<Tutorpackage> Tutorpackages { get; }
    DbSet<Tutorpackagefixedslot> Tutorpackagefixedslots { get; }
    DbSet<Tutorprofile> Tutorprofiles { get; }
    DbSet<Tutorsubjectgradeprice> Tutorsubjectgradeprices { get; }
    DbSet<Tutorsubscription> Tutorsubscriptions { get; }
    DbSet<User> Users { get; }
    DbSet<Userwarning> Userwarnings { get; }
    DbSet<Wallet> Wallets { get; }
    DbSet<Wallettransaction> Wallettransactions { get; }
    DbSet<PaymentRequest> PaymentRequests { get; }
    DbSet<PaymentTransaction> PaymentTransactions { get; }
    DbSet<Withdrawalrequest> Withdrawalrequests { get; }
    DbSet<WithdrawalScore> Withdrawalscores { get; }
    DbSet<Systemalert> Systemalerts { get; }
    DbSet<RefreshToken> Refreshtokens { get; }
    DbSet<StaffPermission> StaffPermissions { get; }
    DbSet<PermissionDefinition> PermissionDefinitions { get; }
    DbSet<PermissionDefinitionRequirement> PermissionDefinitionRequirements { get; }
    DbSet<PermissionGroup> PermissionGroups { get; }
    DbSet<PermissionGroupPermission> PermissionGroupPermissions { get; }
    DbSet<StaffPermissionGroupAssignment> StaffPermissionGroupAssignments { get; }
    DbSet<PermissionAuditLog> PermissionAuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
