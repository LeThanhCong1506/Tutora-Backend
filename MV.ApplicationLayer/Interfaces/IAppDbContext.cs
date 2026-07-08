using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Interfaces;

public interface IAppDbContext
{
    DatabaseFacade Database { get; }

    DbSet<Booking> Bookings { get; }
    DbSet<FraudLog> Fraudlogs { get; }
    DbSet<LoginHistory> Loginhistories { get; }
    DbSet<Chatchannel> Chatchannels { get; }
    DbSet<Chatmessage> Chatmessages { get; }
    DbSet<ChatSession> ChatSessions { get; }
    DbSet<ChatHistory> ChatHistories { get; }
    DbSet<Class> Classes { get; }
    DbSet<Dispute> Disputes { get; }
    DbSet<DisputeEvidence> DisputeEvidences { get; }
    DbSet<Feedback> Feedbacks { get; }
    DbSet<Handoversummary> Handoversummaries { get; }
    DbSet<Learningmaterial> Learningmaterials { get; }
    DbSet<ClassSession> ClassSessions { get; }
    DbSet<ClassSessionReport> ClassSessionReports { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<QuestionBank> QuestionBanks { get; }
    DbSet<AiCreditTransaction> AiCreditTransactions { get; }
    DbSet<Profilesuspension> Profilesuspensions { get; }
    DbSet<Promotion> Promotions { get; }
    DbSet<Gradelevel> Gradelevels { get; }
    DbSet<Studentgrade> Studentgrades { get; }
    DbSet<Studentprofile> Studentprofiles { get; }
    DbSet<Subject> Subjects { get; }
    DbSet<Systemconfig> Systemconfigs { get; }
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
    DbSet<Withdrawalrequest> Withdrawalrequests { get; }
    DbSet<WithdrawalScore> Withdrawalscores { get; }
    DbSet<Systemalert> Systemalerts { get; }
    DbSet<RefreshToken> Refreshtokens { get; }
    DbSet<StaffPermission> StaffPermissions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
