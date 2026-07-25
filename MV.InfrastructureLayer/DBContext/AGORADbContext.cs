using System;
using MV.ApplicationLayer.Interfaces;
using MV.DomainLayer.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace MV.InfrastructureLayer.DBContext;

public partial class AgoraDbContext : DbContext, IAppDbContext
{
    public AgoraDbContext()
    {
    }

    public AgoraDbContext(DbContextOptions<AgoraDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<BankAccount> BankAccounts { get; set; }

    public virtual DbSet<Booking> Bookings { get; set; }

    public virtual DbSet<FraudLog> Fraudlogs { get; set; }

    public virtual DbSet<LoginHistory> Loginhistories { get; set; }

    public virtual DbSet<Chatchannel> Chatchannels { get; set; }

    public virtual DbSet<Chatmessage> Chatmessages { get; set; }

    public virtual DbSet<ChatSession> ChatSessions { get; set; }

    public virtual DbSet<ChatHistory> ChatHistories { get; set; }

    public virtual DbSet<QuestionNote> QuestionNotes { get; set; }

    public virtual DbSet<Class> Classes { get; set; }

    public virtual DbSet<Dispute> Disputes { get; set; }

    public virtual DbSet<DisputeEvidence> DisputeEvidences { get; set; }

    public virtual DbSet<DisputeMessage> DisputeMessages { get; set; }

    public virtual DbSet<Feedback> Feedbacks { get; set; }

    public virtual DbSet<Handoversummary> Handoversummaries { get; set; }

    public virtual DbSet<Learningmaterial> Learningmaterials { get; set; }

    public virtual DbSet<ClassSession> ClassSessions { get; set; }

    public virtual DbSet<ClassSessionReport> ClassSessionReports { get; set; }

    public virtual DbSet<ClassSessionScheduleChange> ClassSessionScheduleChanges { get; set; }

    public virtual DbSet<SessionEngagementSample> SessionEngagementSamples { get; set; }

    public virtual DbSet<AgoraChannelEvent> AgoraChannelEvents { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<QuestionBank> QuestionBanks { get; set; }

    public virtual DbSet<QuestionVote> QuestionVotes { get; set; }

    public virtual DbSet<SourceDocument> SourceDocuments { get; set; }

    public virtual DbSet<Chapter> Chapters { get; set; }

    public virtual DbSet<QuestionType> QuestionTypes { get; set; }

    public virtual DbSet<AiCreditTransaction> AiCreditTransactions { get; set; }

    public virtual DbSet<AiCreditPackage> AiCreditPackages { get; set; }

    public virtual DbSet<Profilesuspension> Profilesuspensions { get; set; }

    public virtual DbSet<Promotion> Promotions { get; set; }

    public virtual DbSet<Gradelevel> Gradelevels { get; set; }

    public virtual DbSet<Dayofweek> DaysOfWeek { get; set; }

    public virtual DbSet<Studentgrade> Studentgrades { get; set; }

    public virtual DbSet<Studentprofile> Studentprofiles { get; set; }

    public virtual DbSet<Subject> Subjects { get; set; }

    public virtual DbSet<Systemconfig> Systemconfigs { get; set; }

    public virtual DbSet<Topuprequest> Topuprequests { get; set; }

    public virtual DbSet<Tutoravailability> Tutoravailabilities { get; set; }

    public virtual DbSet<Tutorcertificate> Tutorcertificates { get; set; }

    public virtual DbSet<Tutorpackage> Tutorpackages { get; set; }

    public virtual DbSet<Tutorpackagefixedslot> Tutorpackagefixedslots { get; set; }

    public virtual DbSet<Tutorprofile> Tutorprofiles { get; set; }

    public virtual DbSet<Tutorsubjectgradeprice> Tutorsubjectgradeprices { get; set; }

    public virtual DbSet<Tutorsubscription> Tutorsubscriptions { get; set; }

    public virtual DbSet<User> Users { get; set; }



    public virtual DbSet<Userwarning> Userwarnings { get; set; }

    public virtual DbSet<Wallet> Wallets { get; set; }

    public virtual DbSet<Wallettransaction> Wallettransactions { get; set; }

    public virtual DbSet<PaymentRequest> PaymentRequests { get; set; }

    public virtual DbSet<PaymentTransaction> PaymentTransactions { get; set; }

    public virtual DbSet<Withdrawalrequest> Withdrawalrequests { get; set; }

    public virtual DbSet<WithdrawalScore> Withdrawalscores { get; set; }

    public virtual DbSet<Systemalert> Systemalerts { get; set; }

    public virtual DbSet<RefreshToken> Refreshtokens { get; set; }

    public virtual DbSet<StaffPermission> StaffPermissions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pgcrypto");

        modelBuilder.Entity<Studentprofile>().HasQueryFilter(e => EF.Property<DateTime?>(e, "Deletedat") == null);
        modelBuilder.Entity<Tutorprofile>().HasQueryFilter(e => EF.Property<DateTime?>(e, "Deletedat") == null);

        modelBuilder.Entity<BankAccount>(entity =>
        {
            entity.HasKey(e => e.Bankaccountid).HasName("bank_accounts_pkey");

            entity.ToTable("bank_accounts");

            entity.HasIndex(e => e.Userid, "bank_accounts_userid_key").IsUnique();

            entity.Property(e => e.Bankaccountid).HasColumnName("bank_account_id");
            entity.Property(e => e.Userid)
                .HasMaxLength(50)
                .HasColumnName("user_id");
            entity.Property(e => e.Bankname)
                .HasMaxLength(100)
                .HasColumnName("bank_name");
            entity.Property(e => e.Accountnumber)
                .HasMaxLength(50)
                .HasColumnName("account_number");
            entity.Property(e => e.Accountholdername)
                .HasMaxLength(100)
                .HasColumnName("account_holder_name");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Updatedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.User).WithOne(p => p.BankAccount)
                .HasForeignKey<BankAccount>(d => d.Userid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("bank_accounts_userid_fkey");
        });
        modelBuilder.Entity<FraudLog>(entity =>
        {
            entity.HasKey(e => e.Logid).HasName("fraud_logs_pkey");

            entity.ToTable("fraud_logs");

            entity.Property(e => e.Logid).HasColumnName("log_id");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutor_id");
            entity.Property(e => e.Withdrawalrequestid).HasColumnName("withdrawal_request_id");
            entity.Property(e => e.Rulename)
                .HasMaxLength(100)
                .HasColumnName("rule_name");
            entity.Property(e => e.Passed).HasColumnName("passed");
            entity.Property(e => e.Isflagged).HasColumnName("is_flagged");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .HasColumnName("metadata");
            entity.Property(e => e.Checkedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("checked_at");

            entity.HasOne(d => d.Tutor).WithMany()
                .HasForeignKey(d => d.Tutorid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_fraud_logs_tutor");

            entity.HasOne(d => d.Withdrawalrequest).WithMany()
                .HasForeignKey(d => d.Withdrawalrequestid)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_fraud_logs_withdrawal");
        });

        modelBuilder.Entity<LoginHistory>(entity =>
        {
            entity.HasKey(e => e.Logid).HasName("login_history_pkey");

            entity.ToTable("login_history");

            entity.Property(e => e.Logid).HasColumnName("log_id");
            entity.Property(e => e.Userid)
                .HasMaxLength(50)
                .HasColumnName("user_id");
            entity.Property(e => e.Ipaddress)
                .HasMaxLength(45)
                .HasColumnName("ip_address");
            entity.Property(e => e.Useragent).HasColumnName("user_agent");
            entity.Property(e => e.Loggedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("logged_at");

            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.Userid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_login_history_user");
        });

        modelBuilder.Entity<SessionEngagementSample>(entity =>
        {
            entity.HasKey(e => e.SampleId).HasName("session_engagement_samples_pkey");

            entity.ToTable("session_engagement_samples");

            entity.Property(e => e.SampleId).HasColumnName("sample_id");
            entity.Property(e => e.ClassSessionId).HasColumnName("class_session_id");
            entity.Property(e => e.StudentUserId)
                .HasMaxLength(50)
                .HasColumnName("student_user_id");
            entity.Property(e => e.Emotion)
                .HasMaxLength(20)
                .HasColumnName("emotion");
            entity.Property(e => e.EngagementScore).HasColumnName("engagement_score");
            entity.Property(e => e.Drowsy).HasColumnName("drowsy");
            entity.Property(e => e.AlertReason)
                .HasMaxLength(20)
                .HasColumnName("alert_reason");
            entity.Property(e => e.SampledAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("sampled_at");

            entity.HasOne(d => d.ClassSession).WithMany()
                .HasForeignKey(d => d.ClassSessionId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_session_engagement_samples_class_session");

            entity.HasIndex(e => new { e.ClassSessionId, e.SampledAt })
                .HasDatabaseName("idx_session_engagement_samples_session_time");
        });

        modelBuilder.Entity<AgoraChannelEvent>(entity =>
        {
            entity.HasKey(e => e.EventId).HasName("agora_channel_events_pkey");

            entity.ToTable("agora_channel_events");

            entity.Property(e => e.EventId)
                .UseIdentityAlwaysColumn()
                .HasColumnName("event_id");
            entity.Property(e => e.NoticeId)
                .HasMaxLength(64)
                .HasColumnName("notice_id");
            entity.Property(e => e.ClassSessionId).HasColumnName("class_session_id");
            entity.Property(e => e.EventType).HasColumnName("event_type");
            entity.Property(e => e.EventAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("event_at");
            entity.Property(e => e.ReceivedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("received_at");
            entity.Property(e => e.Payload)
                .HasColumnType("jsonb")
                .HasColumnName("payload");

            entity.HasIndex(e => e.NoticeId, "ux_agora_events_notice").IsUnique();
            entity.HasIndex(e => new { e.ClassSessionId, e.EventAt }, "idx_agora_events_session");
        });

        modelBuilder.Entity<WithdrawalScore>(entity =>
        {
            entity.HasKey(e => e.Scoreid).HasName("withdrawal_scores_pkey");

            entity.ToTable("withdrawal_scores");

            entity.Property(e => e.Scoreid).HasColumnName("score_id");
            entity.Property(e => e.Withdrawalrequestid).HasColumnName("withdrawal_request_id");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutor_id");
            entity.Property(e => e.Basescore).HasColumnName("base_score");
            entity.Property(e => e.Positivefactors)
                .HasColumnType("jsonb")
                .HasColumnName("positive_factors");
            entity.Property(e => e.Negativefactors)
                .HasColumnType("jsonb")
                .HasColumnName("negative_factors");
            entity.Property(e => e.Fraudflags)
                .HasColumnType("jsonb")
                .HasColumnName("fraud_flags");
            entity.Property(e => e.Totalscore).HasColumnName("total_score");
            entity.Property(e => e.Decision)
                .HasMaxLength(50)
                .HasColumnName("decision");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");

            entity.HasOne(d => d.Withdrawalrequest).WithMany()
                .HasForeignKey(d => d.Withdrawalrequestid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_withdrawal_scores_withdrawal");

            entity.HasOne(d => d.Tutor).WithMany()
                .HasForeignKey(d => d.Tutorid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_withdrawal_scores_tutor");
        });

        modelBuilder.Entity<Booking>(entity =>
        {
            entity.HasKey(e => e.Bookingid).HasName("bookings_pkey");

            entity.ToTable("bookings");

            entity.HasIndex(e => e.Status, "idx_bookings_status");

            entity.Property(e => e.Bookingid).HasColumnName("booking_id");
            entity.Property(e => e.Cancellationreason).HasColumnName("cancellation_reason");
            entity.Property(e => e.Cancelledat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("cancelled_at");
            entity.Property(e => e.Cancelledby)
                .HasMaxLength(50)
                .HasColumnName("cancelled_by");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Depositamount)
                .HasPrecision(12, 2)
                .HasColumnName("deposit_amount");
            entity.Property(e => e.Depositpaidat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("deposit_paid_at");
            entity.Property(e => e.Discountapplied)
                .HasPrecision(18, 2)
                .HasColumnName("discount_applied");

            entity.Property(e => e.Escrowstatus)
                .HasMaxLength(20)
                .HasDefaultValueSql("'held'::character varying")
                .HasColumnName("escrow_status");
            entity.Property(e => e.Finalprice)
                .HasPrecision(12, 2)
                .HasColumnName("final_price");
            entity.Property(e => e.Graceperiodends)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("grace_period_ends");
            entity.Property(e => e.Locationcity)
                .HasMaxLength(50)
                .HasColumnName("location_city");
            entity.Property(e => e.Locationdetail)
                .HasMaxLength(255)
                .HasColumnName("location_detail");
            entity.Property(e => e.Locationdistrict)
                .HasMaxLength(50)
                .HasColumnName("location_district");
            entity.Property(e => e.Locationward)
                .HasMaxLength(50)
                .HasColumnName("location_ward");
            entity.Property(e => e.Packageid).HasColumnName("package_id");
            entity.Property(e => e.Totalsessions).HasColumnName("total_sessions");

            entity.Property(e => e.Priceperhour)
                .HasPrecision(12, 2)
                .HasColumnName("price_per_hour");
            entity.Property(e => e.Totalamount)
                .HasPrecision(12, 2)
                .HasColumnName("total_amount");
            entity.Property(e => e.Currency)
                .HasMaxLength(10)
                .HasDefaultValueSql("'VND'::character varying")
                .HasColumnName("currency");
            entity.Property(e => e.Parentfee)
                .HasPrecision(12, 2)
                .HasColumnName("parent_fee");
            entity.Property(e => e.Parentid)
                .HasMaxLength(50)
                .HasColumnName("parent_id");
            entity.Property(e => e.Startdate)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("start_date");
            entity.Property(e => e.Paymentdueat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("payment_due_at");
            entity.Property(e => e.Paymentstatus)
                .HasMaxLength(20)
                .HasColumnName("payment_status");
            entity.Property(e => e.Platformfee)
                .HasPrecision(12, 2)
                .HasColumnName("platform_fee");
            entity.Property(e => e.Promotionid).HasColumnName("promotion_id");
            entity.Property(e => e.Remainingamount)
                .HasPrecision(12, 2)
                .HasColumnName("remaining_amount");
            entity.Property(e => e.Remainingpaidat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("remaining_paid_at");
            entity.Property(e => e.Sessionsremaining).HasColumnName("sessions_remaining");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasColumnName("status")
                .IsConcurrencyToken();
            entity.Property(e => e.Studentid)
                .HasMaxLength(50)
                .HasColumnName("student_id");
            entity.Property(e => e.Createdbyrole)
                .HasMaxLength(20)
                .HasColumnName("created_by_role");
            entity.Property(e => e.Responsedeadline)
                .HasColumnName("response_deadline");
            entity.Property(e => e.Tutorsubjectgradepriceid).HasColumnName("tutor_subject_grade_price_id");
            entity.Property(e => e.Tutorfee)
                .HasPrecision(12, 2)
                .HasColumnName("tutor_fee");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutor_id");
            entity.Property(e => e.Updatedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.Refundamount)
                .HasPrecision(12, 2)
                .HasColumnName("refund_amount");
            entity.Property(e => e.Refundstatus)
                .HasMaxLength(50)
                .HasColumnName("refund_status");

            entity.HasOne(d => d.Parent).WithMany(p => p.Bookings)
                .HasForeignKey(d => d.Parentid)
                .HasConstraintName("bookings_parentid_fkey");

            entity.HasOne(d => d.Promotion).WithMany(p => p.Bookings)
                .HasForeignKey(d => d.Promotionid)
                .HasConstraintName("bookings_promotionid_fkey");

            entity.HasOne(d => d.Student).WithMany(p => p.Bookings)
                .HasForeignKey(d => d.Studentid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("bookings_studentid_fkey");

            entity.HasOne(d => d.Package).WithMany(p => p.Bookings)
                .HasForeignKey(d => d.Packageid)
                .HasConstraintName("fk_bookings_package");

            entity.HasOne(d => d.Tutor).WithMany(p => p.Bookings)
                .HasForeignKey(d => d.Tutorid)
                .HasConstraintName("bookings_tutorid_fkey");

            entity.HasOne(d => d.Tutorsubjectgradeprice).WithMany(p => p.Bookings)
                .HasForeignKey(d => d.Tutorsubjectgradepriceid)
                .HasConstraintName("bookings_tutorsubjectgradepriceid_fkey");
        });

        modelBuilder.Entity<Chatchannel>(entity =>
        {
            entity.HasKey(e => e.Channelid).HasName("chatchannels_pkey");

            entity.ToTable("chat_channels");

            entity.Property(e => e.Channelid).HasColumnName("channel_id");
            entity.Property(e => e.Bookingid).HasColumnName("booking_id");
            entity.Property(e => e.Parentid)
                .HasMaxLength(50)
                .HasColumnName("parent_id");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutor_id");
            // Phase 4: Uncomment khi DB có cột studentid trên chatchannels
            entity.Property(e => e.Studentid)
                .HasMaxLength(50)
                .HasColumnName("student_id");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Lastmessageat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("last_message_at");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'active'::character varying")
                .HasColumnName("status");

            entity.HasIndex(e => new { e.Parentid, e.Tutorid })
                .IsUnique()
                .HasFilter("parent_id IS NOT NULL AND tutor_id IS NOT NULL")
                .HasDatabaseName("ix_chatchannels_parentid_tutorid");

            // Phase 4: Uncomment khi DB có cột studentid trên chatchannels
            entity.HasIndex(e => new { e.Studentid, e.Tutorid })
                .IsUnique()
                .HasFilter("student_id IS NOT NULL AND tutor_id IS NOT NULL")
                .HasDatabaseName("ix_chatchannels_studentid_tutorid");

            entity.HasOne(d => d.Booking).WithMany(p => p.Chatchannels)
                .HasForeignKey(d => d.Bookingid)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("chatchannels_bookingid_fkey");

            entity.HasOne(d => d.Parent).WithMany()
                .HasForeignKey(d => d.Parentid)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("chatchannels_parentid_fkey");

            entity.HasOne(d => d.Tutor).WithMany()
                .HasForeignKey(d => d.Tutorid)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("chatchannels_tutorid_fkey");

            // Phase 4: Uncomment khi DB có cột studentid trên chatchannels
            entity.HasOne(d => d.Student).WithMany()
                .HasForeignKey(d => d.Studentid)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("chatchannels_studentid_fkey");

            entity.HasMany(d => d.Users).WithMany(p => p.Channels)
                .UsingEntity<Dictionary<string, object>>(
                    "Chatparticipant",
                    r => r.HasOne<User>().WithMany()
                        .HasForeignKey("Userid")
                        .HasConstraintName("chatparticipants_userid_fkey"),
                    l => l.HasOne<Chatchannel>().WithMany()
                        .HasForeignKey("Channelid")
                        .HasConstraintName("chatparticipants_channelid_fkey"),
                    j =>
                    {
                        j.HasKey("Channelid", "Userid").HasName("chatparticipants_pkey");
                        j.ToTable("chat_participants");
                        j.IndexerProperty<int>("Channelid").HasColumnName("channel_id");
                        j.IndexerProperty<string>("Userid")
                            .HasMaxLength(50)
                            .HasColumnName("user_id");
                    });
        });

        modelBuilder.Entity<Chatmessage>(entity =>
        {
            entity.HasKey(e => e.Messageid).HasName("chatmessages_pkey");

            entity.ToTable("chat_messages");

            entity.Property(e => e.Messageid).HasColumnName("message_id");
            entity.Property(e => e.Channelid).HasColumnName("channel_id");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .HasColumnName("metadata");
            entity.Property(e => e.Messagetype)
                .HasMaxLength(20)
                .HasDefaultValueSql("'text'::character varying")
                .HasColumnName("message_type");
            entity.Property(e => e.Senderid)
                .HasMaxLength(50)
                .HasColumnName("sender_id");
            entity.Property(e => e.Isread)
                .HasDefaultValue(false)
                .HasColumnName("is_read");
            entity.Property(e => e.Readat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("read_at");

            entity.HasOne(d => d.Channel).WithMany(p => p.Chatmessages)
                .HasForeignKey(d => d.Channelid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("chatmessages_channelid_fkey");

            entity.HasOne(d => d.Sender).WithMany(p => p.Chatmessages)
                .HasForeignKey(d => d.Senderid)
                .HasConstraintName("chatmessages_senderid_fkey");
        });

        modelBuilder.Entity<ChatSession>(entity =>
        {
            entity.HasKey(e => e.SessionId).HasName("pk_chat_sessions");

            entity.ToTable("chat_sessions");

            entity.Property(e => e.SessionId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("session_id");
            entity.Property(e => e.UserId)
                .HasMaxLength(50)
                .HasColumnName("user_id");
            entity.Property(e => e.SessionType)
                .HasMaxLength(30)
                .HasDefaultValueSql("'homework'::character varying")
                .HasColumnName("session_type");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnName("metadata");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("updated_at");

            entity.HasIndex(e => e.UserId, "idx_chat_sessions_user_id");
            entity.HasIndex(e => e.SessionType, "idx_chat_sessions_type");

            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_chat_sessions_user");
        });

        modelBuilder.Entity<QuestionNote>(entity =>
        {
            entity.HasKey(e => e.NoteId).HasName("question_notes_pkey");

            entity.ToTable("question_notes");

            entity.Property(e => e.NoteId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("note_id");
            entity.Property(e => e.UserId)
                .HasMaxLength(50)
                .HasColumnName("user_id");
            entity.Property(e => e.SourceSessionId)
                .HasColumnName("source_session_id");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");
            entity.Property(e => e.ProblemText)
                .HasColumnName("problem_text");
            entity.Property(e => e.ProblemImageUrl)
                .HasColumnName("problem_image_url");
            entity.Property(e => e.SolutionSteps)
                .HasColumnType("jsonb")
                .HasDefaultValueSql("'[]'::jsonb")
                .HasColumnName("solution_steps");
            entity.Property(e => e.AnswerSummary)
                .HasColumnName("answer_summary");
            entity.Property(e => e.PersonalNote)
                .HasColumnName("personal_note");
            entity.Property(e => e.Subject)
                .HasMaxLength(100)
                .HasColumnName("subject");
            entity.Property(e => e.GradeLevel)
                .HasColumnName("grade_level");
            entity.Property(e => e.Chapter)
                .HasMaxLength(255)
                .HasColumnName("chapter");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "idx_question_notes_user_created");

            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("question_notes_user_id_fkey");
        });

        modelBuilder.Entity<ChatHistory>(entity =>
        {
            entity.HasKey(e => e.MessageId).HasName("pk_chat_histories");

            entity.ToTable("chat_histories");

            entity.Property(e => e.MessageId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("message_id");
            entity.Property(e => e.SessionId).HasColumnName("session_id");
            entity.Property(e => e.Role)
                .HasMaxLength(20)
                .HasColumnName("role");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.ImageUrl)
                .HasMaxLength(1000)
                .HasColumnName("image_url");
            entity.Property(e => e.Grade)
                .HasMaxLength(50)
                .HasColumnName("grade");
            entity.Property(e => e.RagUsed)
                .HasDefaultValue(false)
                .HasColumnName("rag_used");
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnName("metadata");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("created_at");

            entity.HasIndex(e => new { e.SessionId, e.CreatedAt }, "idx_chat_histories_session_id");
            entity.HasIndex(e => e.RagUsed, "idx_chat_histories_rag_used");

            entity.HasOne(d => d.Session).WithMany(p => p.ChatHistories)
                .HasForeignKey(d => d.SessionId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_chat_histories_session");
        });

        modelBuilder.Entity<Class>(entity =>
        {
            entity.HasKey(e => e.Classid).HasName("classes_pkey");

            entity.ToTable("classes");

            entity.HasIndex(e => e.Bookingid, "classes_bookingid_key").IsUnique();

            entity.HasIndex(e => e.Classcode, "classes_classcode_key").IsUnique();

            entity.HasIndex(e => e.Classcode, "idx_classes_code");

            entity.Property(e => e.Classid).HasColumnName("class_id");
            entity.Property(e => e.Bookingid).HasColumnName("booking_id");
            entity.Property(e => e.Classcode)
                .HasMaxLength(20)
                .HasColumnName("class_code");
            entity.Property(e => e.Classname)
                .HasMaxLength(200)
                .HasColumnName("class_name");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'active'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutor_id");

            entity.HasOne(d => d.Booking).WithOne(p => p.Class)
                .HasForeignKey<Class>(d => d.Bookingid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("classes_bookingid_fkey");

            entity.HasOne(d => d.Tutor).WithMany(p => p.Classes)
                .HasForeignKey(d => d.Tutorid)
                .HasConstraintName("classes_tutorid_fkey");
        });

        modelBuilder.Entity<Dispute>(entity =>
        {
            entity.HasKey(e => e.Disputeid).HasName("disputes_pkey");

            entity.ToTable("disputes");

            entity.HasIndex(e => e.Classsessionid, "idx_disputes_class_session_id");

            entity.Property(e => e.Disputeid).HasColumnName("dispute_id");
            entity.Property(e => e.Bookingid).HasColumnName("booking_id");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Createdby)
                .HasMaxLength(50)
                .HasColumnName("created_by");
            entity.Property(e => e.Disputetype)
                .HasMaxLength(50)
                .HasColumnName("dispute_type");
            entity.Property(e => e.Evidence)
                .HasColumnType("jsonb")
                .HasColumnName("evidence");
            entity.Property(e => e.Classsessionid).HasColumnName("class_session_id");
            entity.Property(e => e.Reason).HasColumnName("reason");
            entity.Property(e => e.Refundamount)
                .HasPrecision(12, 2)
                .HasColumnName("refund_amount");
            entity.Property(e => e.Refundissued)
                .HasDefaultValue(false)
                .HasColumnName("refund_issued");
            entity.Property(e => e.Refundpercentage).HasColumnName("refund_percentage");
            entity.Property(e => e.Resolutionnote).HasColumnName("resolution_note");
            entity.Property(e => e.Resolvedat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("resolved_at");
            entity.Property(e => e.Resolvedby)
                .HasMaxLength(50)
                .HasColumnName("resolved_by");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasColumnName("status");
            entity.Property(e => e.Tutorresponse).HasColumnName("tutor_response");
            entity.Property(e => e.Tutorrespondedat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("tutor_responded_at");
            entity.Property(e => e.Noshowconfirmedat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("no_show_confirmed_at");
            entity.Property(e => e.Noshowconfirmedby)
                .HasMaxLength(50)
                .HasColumnName("no_show_confirmed_by");
            entity.Property(e => e.Priority)
                .HasMaxLength(20)
                .HasColumnName("priority");
            entity.Property(e => e.Priorityreason)
                .HasMaxLength(500)
                .HasColumnName("priority_reason");

            entity.HasOne(d => d.Booking).WithMany(p => p.Disputes)
                .HasForeignKey(d => d.Bookingid)
                .HasConstraintName("disputes_bookingid_fkey");

            entity.HasOne(d => d.CreatedbyNavigation).WithMany(p => p.DisputeCreatedbyNavigations)
                .HasForeignKey(d => d.Createdby)
                .HasConstraintName("disputes_createdby_fkey");

            entity.HasOne(d => d.ClassSession).WithMany(p => p.Disputes)
                .HasForeignKey(d => d.Classsessionid)
                .HasConstraintName("disputes_lessonid_fkey");

            entity.HasOne(d => d.ResolvedbyNavigation).WithMany(p => p.DisputeResolvedbyNavigations)
                .HasForeignKey(d => d.Resolvedby)
                .HasConstraintName("disputes_resolvedby_fkey");
        });

        modelBuilder.Entity<DisputeEvidence>(entity =>
        {
            entity.HasKey(e => e.Disputeevidenceid).HasName("dispute_evidences_pkey");

            entity.ToTable("dispute_evidences");

            entity.HasIndex(e => e.Disputeid, "idx_dispute_evidences_dispute");

            entity.Property(e => e.Disputeevidenceid).HasColumnName("dispute_evidence_id");
            entity.Property(e => e.Disputeid).HasColumnName("dispute_id");
            entity.Property(e => e.Uploadedby)
                .HasMaxLength(50)
                .HasColumnName("uploaded_by");
            entity.Property(e => e.Fileurl).HasColumnName("file_url");
            entity.Property(e => e.Filetype)
                .HasMaxLength(50)
                .HasColumnName("file_type");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");

            entity.HasOne(d => d.Dispute).WithMany(p => p.DisputeEvidences)
                .HasForeignKey(d => d.Disputeid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("dispute_evidences_disputeid_fkey");

            entity.HasOne(d => d.UploadedbyNavigation).WithMany(p => p.DisputeEvidences)
                .HasForeignKey(d => d.Uploadedby)
                .HasConstraintName("dispute_evidences_uploadedby_fkey");
        });

        modelBuilder.Entity<DisputeMessage>(entity =>
        {
            entity.HasKey(e => e.Disputemessageid).HasName("dispute_messages_pkey");

            entity.ToTable("dispute_messages");

            entity.HasIndex(e => new { e.Disputeid, e.Threadtype }, "idx_dispute_messages_thread");

            entity.Property(e => e.Disputemessageid).HasColumnName("dispute_message_id");
            entity.Property(e => e.Disputeid).HasColumnName("dispute_id");
            entity.Property(e => e.Threadtype)
                .HasMaxLength(20)
                .HasColumnName("thread_type");
            entity.Property(e => e.Senderid)
                .HasMaxLength(50)
                .HasColumnName("sender_id");
            entity.Property(e => e.Senderrole)
                .HasMaxLength(20)
                .HasColumnName("sender_role");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");

            entity.HasOne(d => d.Dispute).WithMany(p => p.DisputeMessages)
                .HasForeignKey(d => d.Disputeid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("dispute_messages_disputeid_fkey");

            entity.HasOne(d => d.SenderidNavigation).WithMany()
                .HasForeignKey(d => d.Senderid)
                .HasConstraintName("dispute_messages_senderid_fkey");
        });

        modelBuilder.Entity<Feedback>(entity =>
        {
            entity.HasKey(e => e.Feedbackid).HasName("feedbacks_pkey");

            entity.ToTable("feedbacks");

            entity.HasIndex(e => new { e.Bookingid, e.Fromuserid }, "feedbacks_bookingid_fromuserid_key").IsUnique();

            entity.Property(e => e.Feedbackid).HasColumnName("feedback_id");
            entity.Property(e => e.Bookingid).HasColumnName("booking_id");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Feedbacktype)
                .HasMaxLength(30)
                .HasDefaultValueSql("'post_lesson'::character varying")
                .HasColumnName("feedback_type");
            entity.Property(e => e.Fromuserid)
                .HasMaxLength(50)
                .HasColumnName("from_user_id");
            entity.Property(e => e.Isvisible)
                .HasDefaultValue(true)
                .HasColumnName("is_visible");
            entity.Property(e => e.Classsessionid).HasColumnName("class_session_id");
            entity.Property(e => e.Rating).HasColumnName("rating");
            entity.Property(e => e.Repliedat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("replied_at");
            entity.Property(e => e.Replycomment).HasColumnName("reply_comment");
            entity.Property(e => e.Touserid)
                .HasMaxLength(50)
                .HasColumnName("to_user_id");
            
            // --- CẤU HÌNH MAPPING MỚI ---
            entity.Property(e => e.InitialGoal)
                .HasColumnName("initial_goal"); // Mapping với kiểu TEXT

            entity.Property(e => e.ActualResult)
                .HasColumnName("actual_result"); // Mapping với kiểu TEXT

            entity.Property(e => e.CourseDuration)
                .HasMaxLength(50)
                .HasColumnName("course_duration"); // Mapping với kiểu VARCHAR(50)
            // ----------------------------

            entity.HasOne(d => d.Booking).WithMany(p => p.Feedbacks)
                .HasForeignKey(d => d.Bookingid)
                .HasConstraintName("feedbacks_bookingid_fkey");

            entity.HasOne(d => d.Fromuser).WithMany(p => p.FeedbackFromusers)
                .HasForeignKey(d => d.Fromuserid)
                .HasConstraintName("feedbacks_fromuserid_fkey");

            entity.HasOne(d => d.ClassSession).WithMany(p => p.Feedbacks)
                .HasForeignKey(d => d.Classsessionid)
                .HasConstraintName("feedbacks_lessonid_fkey");

            entity.HasOne(d => d.Touser).WithMany(p => p.FeedbackTousers)
                .HasForeignKey(d => d.Touserid)
                .HasConstraintName("feedbacks_touserid_fkey");
        });

        modelBuilder.Entity<Handoversummary>(entity =>
        {
            entity.HasKey(e => e.Summaryid).HasName("handoversummaries_pkey");

            entity.ToTable("handover_summaries");

            entity.Property(e => e.Summaryid).HasColumnName("summary_id");
            entity.Property(e => e.Attendancerate)
                .HasPrecision(5, 2)
                .HasColumnName("attendance_rate");
            entity.Property(e => e.Averagescore)
                .HasPrecision(5, 2)
                .HasColumnName("average_score");
            entity.Property(e => e.Frombookingid).HasColumnName("from_booking_id");
            entity.Property(e => e.Fromtutorid)
                .HasMaxLength(50)
                .HasColumnName("from_tutor_id");
            entity.Property(e => e.Generatedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("generated_at");
            entity.Property(e => e.Scoretrend)
                .HasMaxLength(20)
                .HasColumnName("score_trend");
            entity.Property(e => e.Studentid)
                .HasMaxLength(50)
                .HasColumnName("student_id");
            entity.Property(e => e.Topicscovered).HasColumnName("topics_covered");
            entity.Property(e => e.Totalsessions).HasColumnName("total_sessions");
            entity.Property(e => e.Totutorid)
                .HasMaxLength(50)
                .HasColumnName("to_tutor_id");
            entity.Property(e => e.Tutornotes).HasColumnName("tutor_notes");

            entity.HasOne(d => d.Frombooking).WithMany(p => p.Handoversummaries)
                .HasForeignKey(d => d.Frombookingid)
                .HasConstraintName("handoversummaries_frombookingid_fkey");

            entity.HasOne(d => d.Fromtutor).WithMany(p => p.HandoversummaryFromtutors)
                .HasForeignKey(d => d.Fromtutorid)
                .HasConstraintName("handoversummaries_fromtutorid_fkey");

            entity.HasOne(d => d.Student).WithMany(p => p.Handoversummaries)
                .HasForeignKey(d => d.Studentid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("handoversummaries_studentid_fkey");

            entity.HasOne(d => d.Totutor).WithMany(p => p.HandoversummaryTotutors)
                .HasForeignKey(d => d.Totutorid)
                .HasConstraintName("handoversummaries_totutorid_fkey");
        });

        modelBuilder.Entity<Learningmaterial>(entity =>
        {
            entity.HasKey(e => e.Materialid).HasName("learningmaterials_pkey");

            entity.ToTable("learning_materials");

            entity.HasIndex(e => e.Studentid, "idx_learningmaterials_student");

            entity.Property(e => e.Materialid).HasColumnName("material_id");
            entity.Property(e => e.Bookingid).HasColumnName("booking_id");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Filesize).HasColumnName("file_size");
            entity.Property(e => e.Filetype)
                .HasMaxLength(50)
                .HasColumnName("file_type");
            entity.Property(e => e.Fileurl).HasColumnName("file_url");
            entity.Property(e => e.Ispublic)
                .HasDefaultValue(false)
                .HasColumnName("is_public");
            entity.Property(e => e.Ownertype)
                .HasMaxLength(20)
                .HasColumnName("owner_type");
            entity.Property(e => e.Studentid)
                .HasMaxLength(50)
                .HasColumnName("student_id");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");
            entity.Property(e => e.Uploadedby)
                .HasMaxLength(50)
                .HasColumnName("uploaded_by");

            entity.HasOne(d => d.Booking).WithMany(p => p.Learningmaterials)
                .HasForeignKey(d => d.Bookingid)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("learningmaterials_bookingid_fkey");

            entity.HasOne(d => d.Student).WithMany(p => p.Learningmaterials)
                .HasForeignKey(d => d.Studentid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("learningmaterials_studentid_fkey");

            entity.HasOne(d => d.UploadedbyNavigation).WithMany(p => p.Learningmaterials)
                .HasForeignKey(d => d.Uploadedby)
                .HasConstraintName("learningmaterials_uploadedby_fkey");
        });

        modelBuilder.Entity<ClassSession>(entity =>
        {
            entity.HasKey(e => e.Classsessionid).HasName("lessons_pkey");

            entity.ToTable("class_sessions");

            entity.HasIndex(e => new { e.Istutorpresent, e.Isstudentpresent }, "idx_lessons_attendance");

            entity.HasIndex(e => new { e.Tutorid, e.Studentid }, "idx_class_sessions_taught_stats")
                .HasFilter("(status = 'completed' AND is_settled = true)");

            entity.HasIndex(e => e.Autoreportsent, "idx_lessons_autoreport").HasFilter("(auto_report_sent = false)");

            entity.Property(e => e.Classsessionid).HasColumnName("class_session_id");
            entity.Property(e => e.Attendancenote).HasColumnName("attendance_note");
            entity.Property(e => e.Autoreportsent)
                .HasDefaultValue(false)
                .HasColumnName("auto_report_sent");
            entity.Property(e => e.Autoreportsentat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("auto_report_sent_at");
            entity.Property(e => e.Bookingid).HasColumnName("booking_id");
            entity.Property(e => e.Checkintime)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("check_in_time");
            entity.Property(e => e.Checkouttime)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("check_out_time");
            entity.Property(e => e.Confirmdeadline)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("confirm_deadline");
            entity.Property(e => e.Homework).HasColumnName("homework");
            entity.Property(e => e.Ismakeup)
                .HasDefaultValue(false)
                .HasColumnName("is_makeup");
            entity.Property(e => e.Issettled)
                .HasDefaultValue(false)
                .HasColumnName("is_settled");
            entity.Property(e => e.Isstudentpresent).HasColumnName("is_student_present");
            entity.Property(e => e.Istutorpresent).HasColumnName("is_tutor_present");
            entity.Property(e => e.Lessoncontent).HasColumnName("lesson_content");
            entity.Property(e => e.Lessonprice)
                .HasPrecision(12, 2)
                .HasColumnName("lesson_price");
            entity.Property(e => e.Meetinglink)
                .HasMaxLength(1000)
                .HasColumnName("meeting_link");
            entity.Property(e => e.Recordingresourceid)
                .HasMaxLength(255)
                .HasColumnName("recording_resource_id");
            entity.Property(e => e.Recordingsid)
                .HasMaxLength(255)
                .HasColumnName("recording_sid");
            entity.Property(e => e.Recordingurl)
                .HasMaxLength(1000)
                .HasColumnName("recording_url");
            entity.Property(e => e.Recordings3key)
                .HasMaxLength(500)
                .HasColumnName("recording_s3key");
            entity.Property(e => e.Whiteboardroomuuid)
                .HasMaxLength(50)
                .HasColumnName("whiteboard_room_uuid");
            entity.Property(e => e.Noshowaction)
                .HasMaxLength(30)
                .HasColumnName("no_show_action");
            entity.Property(e => e.Originalsessionid).HasColumnName("original_session_id");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Parentackat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("parent_ack_at");
            entity.Property(e => e.Realend)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("real_end");
            entity.Property(e => e.Realstart)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("real_start");
            entity.Property(e => e.Receiptsentat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("receipt_sent_at");
            entity.Property(e => e.Scheduledend)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("scheduled_end");
            entity.Property(e => e.Scheduledstart)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("scheduled_start");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasDefaultValueSql("'scheduled'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.Studentid)
                .HasMaxLength(50)
                .HasColumnName("student_id");
            entity.Property(e => e.Submittedat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("submitted_at");
            entity.Property(e => e.Isearlysubmission)
                .HasColumnName("is_early_submission");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutor_id");
            entity.Property(e => e.Tutornotes).HasColumnName("tutor_notes");

            entity.HasOne(d => d.Booking).WithMany(p => p.ClassSessions)
                .HasForeignKey(d => d.Bookingid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("lessons_bookingid_fkey");

            entity.HasOne(d => d.Originalsession).WithMany(p => p.InverseOriginalsession)
                .HasForeignKey(d => d.Originalsessionid)
                .HasConstraintName("lessons_originallessonid_fkey");

            entity.HasOne(d => d.Student).WithMany(p => p.ClassSessions)
                .HasForeignKey(d => d.Studentid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("lessons_studentid_fkey");

            entity.HasOne(d => d.Tutor).WithMany(p => p.ClassSessions)
                .HasForeignKey(d => d.Tutorid)
                .HasConstraintName("lessons_tutorid_fkey");
        });

        modelBuilder.Entity<ClassSessionReport>(entity =>
        {
            entity.HasKey(e => e.Reportid).HasName("lessonreports_pkey");

            entity.ToTable("class_session_reports");

            entity.HasIndex(e => e.Classsessionid, "lessonreports_lessonid_key").IsUnique();

            entity.Property(e => e.Reportid).HasColumnName("report_id");
            entity.Property(e => e.Attachments)
                .HasColumnType("jsonb")
                .HasColumnName("attachments");
            entity.Property(e => e.Contentcovered).HasColumnName("content_covered");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Createdbytutorid)
                .HasMaxLength(50)
                .HasColumnName("created_by_tutor_id");
            entity.Property(e => e.Homeworkassigned).HasColumnName("homework_assigned");
            entity.Property(e => e.Classsessionid).HasColumnName("class_session_id");
            entity.Property(e => e.Studentperformancerating).HasColumnName("student_performance_rating");

            entity.HasOne(d => d.Createdbytutor).WithMany(p => p.ClassSessionReports)
                .HasForeignKey(d => d.Createdbytutorid)
                .HasConstraintName("lessonreports_createdbytutorid_fkey");

            entity.HasOne(d => d.ClassSession).WithOne(p => p.ClassSessionReport)
                .HasForeignKey<ClassSessionReport>(d => d.Classsessionid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("lessonreports_lessonid_fkey");
        });

        modelBuilder.Entity<ClassSessionScheduleChange>(entity =>
        {
            entity.HasKey(e => e.Schedulechangeid).HasName("class_session_schedule_changes_pkey");
            entity.ToTable("class_session_schedule_changes");
            entity.HasIndex(e => e.Classsessionid, "idx_schedule_changes_session");
            entity.HasIndex(e => new { e.Classsessionid, e.Status }, "idx_schedule_changes_active");

            entity.Property(e => e.Schedulechangeid).HasColumnName("schedule_change_id");
            entity.Property(e => e.Classsessionid).HasColumnName("class_session_id");
            entity.Property(e => e.Originalscheduledstart).HasColumnType("timestamp without time zone").HasColumnName("original_scheduled_start");
            entity.Property(e => e.Originalscheduledend).HasColumnType("timestamp without time zone").HasColumnName("original_scheduled_end");
            entity.Property(e => e.Tutoruserid).HasMaxLength(50).HasColumnName("tutor_user_id");
            entity.Property(e => e.Learnerapproveruserid).HasMaxLength(50).HasColumnName("learner_approver_user_id");
            entity.Property(e => e.Learnerapproverrole).HasMaxLength(20).HasColumnName("learner_approver_role");
            entity.Property(e => e.Tutorconfirmedat).HasColumnType("timestamp without time zone").HasColumnName("tutor_confirmed_at");
            entity.Property(e => e.Tutorconfirmedby).HasMaxLength(50).HasColumnName("tutor_confirmed_by");
            entity.Property(e => e.Learnerconfirmedat).HasColumnType("timestamp without time zone").HasColumnName("learner_confirmed_at");
            entity.Property(e => e.Learnerconfirmedby).HasMaxLength(50).HasColumnName("learner_confirmed_by");
            entity.Property(e => e.Rejectedat).HasColumnType("timestamp without time zone").HasColumnName("rejected_at");
            entity.Property(e => e.Rejectedby).HasMaxLength(50).HasColumnName("rejected_by");
            entity.Property(e => e.Requestedat).HasColumnType("timestamp without time zone").HasColumnName("requested_at");
            entity.Property(e => e.Expiresat).HasColumnType("timestamp without time zone").HasColumnName("expires_at");
            entity.Property(e => e.Approvedat).HasColumnType("timestamp without time zone").HasColumnName("approved_at");
            entity.Property(e => e.Appliedat).HasColumnType("timestamp without time zone").HasColumnName("applied_at");
            entity.Property(e => e.Adjustedscheduledstart).HasColumnType("timestamp without time zone").HasColumnName("adjusted_scheduled_start");
            entity.Property(e => e.Adjustedscheduledend).HasColumnType("timestamp without time zone").HasColumnName("adjusted_scheduled_end");
            entity.Property(e => e.Status).HasMaxLength(20).HasColumnName("status");
            entity.Property(e => e.Createdat).HasColumnType("timestamp without time zone").HasColumnName("created_at");
            entity.Property(e => e.Updatedat).HasColumnType("timestamp without time zone").HasColumnName("updated_at");

            entity.HasOne(e => e.ClassSession)
                .WithMany(e => e.ScheduleChanges)
                .HasForeignKey(e => e.Classsessionid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("class_session_schedule_changes_session_fkey");
        });
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Notificationid).HasName("notifications_pkey");

            entity.ToTable("notifications");

            entity.HasIndex(e => new { e.Userid, e.Isread }, "idx_notifications_user");

            entity.Property(e => e.Notificationid).HasColumnName("notification_id");
            entity.Property(e => e.Channel)
                .HasMaxLength(30)
                .HasDefaultValueSql("'app'::character varying")
                .HasColumnName("channel");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Deliverystatus)
                .HasMaxLength(20)
                .HasColumnName("delivery_status");
            entity.Property(e => e.Isread)
                .HasDefaultValue(false)
                .HasColumnName("is_read");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.Referenceid)
                .HasMaxLength(50)
                .HasColumnName("reference_id");
            entity.Property(e => e.Title)
                .HasMaxLength(200)
                .HasColumnName("title");
            entity.Property(e => e.Type)
                .HasMaxLength(50)
                .HasColumnName("type");
            entity.Property(e => e.Userid)
                .HasMaxLength(50)
                .HasColumnName("user_id");
            entity.Property(e => e.Zaborequestid)
                .HasMaxLength(100)
                .HasColumnName("zabo_request_id");

            entity.HasOne(d => d.User).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.Userid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("notifications_userid_fkey");
        });

        modelBuilder.Entity<Profilesuspension>(entity =>
        {
            entity.HasKey(e => e.Suspensionid).HasName("profilesuspensions_pkey");

            entity.ToTable("profile_suspensions");

            entity.Property(e => e.Suspensionid).HasColumnName("suspension_id");
            entity.Property(e => e.Createdby)
                .HasMaxLength(50)
                .HasColumnName("created_by");
            entity.Property(e => e.Enddate)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("end_date");
            entity.Property(e => e.Isactive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Reason).HasColumnName("reason");
            entity.Property(e => e.Startdate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("start_date");
            entity.Property(e => e.Suspensiontype)
                .HasMaxLength(30)
                .HasColumnName("suspension_type");
            entity.Property(e => e.Userid)
                .HasMaxLength(50)
                .HasColumnName("user_id");

            entity.HasOne(d => d.CreatedbyNavigation).WithMany(p => p.ProfilesuspensionCreatedbyNavigations)
                .HasForeignKey(d => d.Createdby)
                .HasConstraintName("profilesuspensions_createdby_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.ProfilesuspensionUsers)
                .HasForeignKey(d => d.Userid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("profilesuspensions_userid_fkey");
        });

        modelBuilder.Entity<Promotion>(entity =>
        {
            entity.HasKey(e => e.Promotionid).HasName("promotions_pkey");

            entity.ToTable("promotions");

            entity.HasIndex(e => e.Code, "idx_promotions_code");

            entity.HasIndex(e => e.Code, "promotions_code_key").IsUnique();

            entity.Property(e => e.Promotionid)
                .UseIdentityByDefaultColumn()
                .HasColumnName("promotion_id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Discounttype)
                .HasMaxLength(20)
                .HasColumnName("discount_type");
            entity.Property(e => e.Discountvalue)
                .HasPrecision(12, 2)
                .HasColumnName("discount_value");
            entity.Property(e => e.Enddate)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("end_date");
            entity.Property(e => e.Isactive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Maxdiscountamount)
                .HasPrecision(12, 2)
                .HasColumnName("max_discount_amount");
            entity.Property(e => e.Minordervalue)
                .HasPrecision(12, 2)
                .HasColumnName("min_order_value");
            entity.Property(e => e.Startdate)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("start_date");
            entity.Property(e => e.Usagecount)
                .HasDefaultValue(0)
                .HasColumnName("usage_count");
            entity.Property(e => e.Usagelimit).HasColumnName("usage_limit");
        });



        modelBuilder.Entity<Studentgrade>(entity =>
        {
            entity.HasKey(e => e.Gradeid).HasName("studentgrades_pkey");

            entity.ToTable("student_grades");

            entity.HasIndex(e => e.Bookingid, "idx_studentgrades_booking");

            entity.HasIndex(e => e.Studentid, "idx_studentgrades_student");

            entity.Property(e => e.Gradeid).HasColumnName("grade_id");
            entity.Property(e => e.Bookingid).HasColumnName("booking_id");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Examdate).HasColumnName("exam_date");
            entity.Property(e => e.Examname)
                .HasMaxLength(200)
                .HasColumnName("exam_name");
            entity.Property(e => e.Examtype)
                .HasMaxLength(50)
                .HasColumnName("exam_type");
            entity.Property(e => e.Maxscore)
                .HasPrecision(5, 2)
                .HasDefaultValueSql("10")
                .HasColumnName("max_score");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.Score)
                .HasPrecision(5, 2)
                .HasColumnName("score");
            entity.Property(e => e.Studentid)
                .HasMaxLength(50)
                .HasColumnName("student_id");
            entity.Property(e => e.Subjectid).HasColumnName("subject_id");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutor_id");

            entity.HasOne(d => d.Booking).WithMany(p => p.Studentgrades)
                .HasForeignKey(d => d.Bookingid)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("studentgrades_bookingid_fkey");

            entity.HasOne(d => d.Student).WithMany(p => p.Studentgrades)
                .HasForeignKey(d => d.Studentid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("studentgrades_studentid_fkey");

            entity.HasOne(d => d.Subject).WithMany(p => p.Studentgrades)
                .HasForeignKey(d => d.Subjectid)
                .HasConstraintName("studentgrades_subjectid_fkey");

            entity.HasOne(d => d.Tutor).WithMany(p => p.Studentgrades)
                .HasForeignKey(d => d.Tutorid)
                .HasConstraintName("studentgrades_tutorid_fkey");
        });

        modelBuilder.Entity<Studentprofile>(entity =>
        {
            entity.HasKey(e => e.Studentid).HasName("studentprofiles_pkey");

            entity.ToTable("student_profiles");

            entity.HasIndex(e => e.Studentcode, "idx_studentprofiles_code");

            entity.HasIndex(e => e.Studentcode, "studentprofiles_studentcode_key").IsUnique();

            entity.Property(e => e.Studentid)
                .HasMaxLength(50)
                .HasColumnName("student_id");
            entity.Property(e => e.Avatarurl)
                .HasMaxLength(1000)
                .HasColumnName("avatar_url");
            entity.Property(e => e.Birthdate).HasColumnName("birth_date");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Fullname)
                .HasMaxLength(100)
                .HasColumnName("full_name");
            entity.Property(e => e.Gradelevelid).HasColumnName("grade_level_id");
            entity.Property(e => e.Learninggoals).HasColumnName("learning_goals");
            entity.Property(e => e.Linkeduserid)
                .HasMaxLength(50)
                .HasColumnName("linked_user_id");
            entity.Property(e => e.Parentid)
                .HasMaxLength(50)
                .HasColumnName("parent_id");
            entity.Property(e => e.School)
                .HasMaxLength(255)
                .HasColumnName("school");
            entity.Property(e => e.Studentcode)
                .HasMaxLength(20)
                .HasColumnName("student_code");
            entity.Property(e => e.Studentcodeexpiresat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("student_code_expires_at");
            entity.Property(e => e.Parentphone)
                .HasMaxLength(20)
                .HasColumnName("parent_phone");
            entity.Property(e => e.Deletedat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("deleted_at");

            entity.HasOne(d => d.Linkeduser).WithMany(p => p.StudentprofileLinkedusers)
                .HasForeignKey(d => d.Linkeduserid)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("studentprofiles_linkeduserid_fkey");

            entity.HasOne(d => d.Parent).WithMany(p => p.StudentprofileParents)
                .HasForeignKey(d => d.Parentid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("studentprofiles_parentid_fkey");

            entity.HasOne(d => d.GradelevelNavigation).WithMany(p => p.Studentprofiles)
                .HasForeignKey(d => d.Gradelevelid)
                .HasConstraintName("studentprofiles_gradelevelid_fkey");
        });

        modelBuilder.Entity<Subject>(entity =>
        {
            entity.HasKey(e => e.Subjectid).HasName("subjects_pkey");

            entity.ToTable("subjects");

            entity.HasIndex(e => e.Subjectname, "subjects_subjectname_key").IsUnique();

            entity.Property(e => e.Subjectid).HasColumnName("subject_id");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Subjectname)
                .HasMaxLength(100)
                .HasColumnName("subject_name");
            entity.Property(e => e.IsActive).HasDefaultValue(true).HasColumnName("is_active");
            entity.Property(e => e.Slug).HasColumnName("slug");
            entity.Property(e => e.IconUrl).HasColumnName("icon_url");
            entity.Property(e => e.IsHomeworkEnabled).HasDefaultValue(false).HasColumnName("is_homework_enabled");
            entity.Property(e => e.DisplayOrder).HasDefaultValue(0).HasColumnName("display_order");
        });

        modelBuilder.Entity<Systemconfig>(entity =>
        {
            entity.HasKey(e => e.Configid).HasName("systemconfigs_pkey");

            entity.ToTable("system_configs");

            entity.HasIndex(e => e.Configkey, "systemconfigs_configkey_key").IsUnique();

            entity.Property(e => e.Configid).HasColumnName("config_id");
            entity.Property(e => e.Configkey)
                .HasMaxLength(100)
                .HasColumnName("config_key");
            entity.Property(e => e.Configvalue).HasColumnName("config_value");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Updatedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.Updatedby)
                .HasMaxLength(50)
                .HasColumnName("updated_by");

            entity.HasOne(d => d.UpdatedbyNavigation).WithMany(p => p.Systemconfigs)
                .HasForeignKey(d => d.Updatedby)
                .HasConstraintName("systemconfigs_updatedby_fkey");
        });

        modelBuilder.Entity<Tutoravailability>(entity =>
        {
            entity.HasKey(e => e.Availabilityid).HasName("tutoravailability_pkey");

            entity.ToTable("tutor_availability");

            entity.Property(e => e.Availabilityid).HasColumnName("availability_id");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Dayofweek).HasColumnName("day_of_week_id");
            entity.Property(e => e.Endtime).HasColumnName("end_time");
            entity.Property(e => e.Starttime).HasColumnName("start_time");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutor_id");

            entity.HasOne(d => d.Tutor).WithMany(p => p.Tutoravailabilities)
                .HasForeignKey(d => d.Tutorid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("tutoravailability_tutorid_fkey");

            entity.HasOne(d => d.DayofweekNavigation).WithMany(p => p.Tutoravailabilities)
                .HasForeignKey(d => d.Dayofweek)
                .HasConstraintName("tutoravailability_dayofweek_fkey");
        });

        modelBuilder.Entity<Dayofweek>(entity =>
        {
            entity.HasKey(e => e.DayofweekId).HasName("days_of_week_pkey");

            entity.ToTable("days_of_week");

            entity.HasIndex(e => e.DayName, "uq_days_of_week_name").IsUnique();
            entity.HasIndex(e => e.DayOrder, "uq_days_of_week_order").IsUnique();

            entity.Property(e => e.DayofweekId).HasColumnName("day_of_week_id").ValueGeneratedNever();
            entity.Property(e => e.DayName)
                .HasMaxLength(20)
                .HasColumnName("day_name");
            entity.Property(e => e.DayOrder).HasColumnName("day_order");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
        });

        modelBuilder.Entity<Tutorcertificate>(entity =>
        {
            entity.HasKey(e => e.Certificateid).HasName("tutorcertificates_pkey");

            entity.ToTable("tutor_certificates");

            entity.HasIndex(e => e.Tutorid, "idx_tutorcertificate_tutorid");

            entity.Property(e => e.Certificateid)
                .HasMaxLength(36)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("certificate_id");
            entity.Property(e => e.Certificatefileurl)
                .HasMaxLength(2000)
                .HasColumnName("certificate_file_url");
            entity.Property(e => e.Certificatename)
                .HasMaxLength(200)
                .HasColumnName("certificate_name");
            entity.Property(e => e.Certificatetype)
                .HasMaxLength(50)
                .HasColumnName("certificate_type");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Credentialid)
                .HasMaxLength(100)
                .HasColumnName("credential_id");
            entity.Property(e => e.Credentialurl)
                .HasMaxLength(2000)
                .HasColumnName("credential_url");
            entity.Property(e => e.Issuingorganization)
                .HasMaxLength(200)
                .HasColumnName("issuing_organization");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(36)
                .HasColumnName("tutor_id");
            entity.Property(e => e.Updatedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.Yearissued).HasColumnName("year_issued");

            // Thêm các thuộc tính mới ở đây
            entity.Property(e => e.Verificationstatus)
                .HasMaxLength(50)
                .HasDefaultValueSql("'pending_review'::character varying")
                .HasColumnName("verification_status");

            entity.Property(e => e.Verificationnote)
                .HasColumnName("verification_note");

            entity.HasOne(d => d.Tutor).WithMany(p => p.Tutorcertificates)
                .HasForeignKey(d => d.Tutorid)
                .HasConstraintName("fk_tutorcertificate_tutor");
        });

        modelBuilder.Entity<Tutorprofile>(entity =>
        {
            entity.HasKey(e => e.Tutorid).HasName("tutorprofiles_pkey");

            entity.ToTable("tutor_profiles");

            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutor_id");
            // map the CLR property to the exact DB column name (lowercase)
            entity.Property(p => p.Reviewedat).HasColumnName("reviewed_at");
            entity.Property(p => p.Reviewedby).HasColumnName("reviewed_by");
            // map other properties as needed
            entity.Property(e => e.Averagerating)
                .HasDefaultValueSql("0.0")
                .HasColumnName("average_rating");
            entity.Property(e => e.Bio).HasColumnName("bio");
            entity.Property(e => e.Completedhours)
                .HasDefaultValue(0)
                .HasColumnName("completed_hours");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Education)
                .HasMaxLength(255)
                .HasColumnName("education");
            entity.Property(e => e.Experience).HasColumnName("experience");
            entity.Property(e => e.Gpa).HasColumnName("gpa");
            entity.Property(e => e.Gpascale).HasColumnName("gpa_scale");
            entity.Property(e => e.Headline)
                .HasMaxLength(200)
                .HasColumnName("headline");

            entity.Property(e => e.Ispublic)
                .HasDefaultValue(false)
                .HasColumnName("is_public");
            entity.Property(e => e.Isacceptingbookings)
                .HasDefaultValue(true)
                .HasColumnName("is_accepting_bookings");
            entity.Property(e => e.Profilestatus)
                .HasMaxLength(30)
                .HasDefaultValueSql("'draft'::character varying")
                .HasColumnName("profile_status");
            entity.Property(e => e.Rejectionnote).HasColumnName("rejection_note");
            entity.Property(e => e.Subscriptiontype)
                .HasMaxLength(30)
                .HasDefaultValueSql("'free'::character varying")
                .HasColumnName("subscription_type");
            entity.Property(e => e.Teachingareacity)
                .HasMaxLength(50)
                .HasColumnName("teaching_area_city");
            entity.Property(e => e.Teachingareadistrict)
                .HasMaxLength(50)
                .HasColumnName("teaching_area_district");
            entity.Property(e => e.Totalreviews)
                .HasDefaultValue(0)
                .HasColumnName("total_reviews");
            entity.Property(e => e.Updatedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.Deletedat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("deleted_at");
            entity.Property(e => e.Videointrourl)
                .HasMaxLength(1000)
                .HasColumnName("video_intro_url");
entity.HasOne(d => d.Tutor).WithOne(p => p.Tutorprofile)
                .HasForeignKey<Tutorprofile>(d => d.Tutorid)
                .HasConstraintName("tutorprofiles_tutorid_fkey");
        });

        modelBuilder.Entity<Gradelevel>(entity =>
        {
            entity.HasKey(e => e.Gradelevelid).HasName("gradelevels_pkey");

            entity.ToTable("grade_levels");

            entity.HasIndex(e => e.Levelorder, "uq_gradelevels_levelorder").IsUnique();
            entity.HasIndex(e => e.Gradename, "uq_gradelevels_gradename").IsUnique();

            entity.Property(e => e.Gradelevelid).HasColumnName("grade_level_id");
            entity.Property(e => e.Gradename)
                .HasMaxLength(100)
                .HasColumnName("grade_name");
            entity.Property(e => e.Levelorder).HasColumnName("level_order");
            entity.Property(e => e.IsActive).HasDefaultValue(true).HasColumnName("is_active");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
        });

        modelBuilder.Entity<QuestionBank>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("questions_pkey");

            entity.ToTable("questions");

            entity.HasIndex(e => new { e.SubjectId, e.GradeLevelId }, "idx_questions_subject_grade");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.SubjectId).HasColumnName("subject_id");
            entity.Property(e => e.GradeLevelId).HasColumnName("grade_level_id");
            entity.Property(e => e.Chapter).HasColumnName("chapter");
            entity.Property(e => e.ProblemType).HasColumnName("problem_type");
            entity.Property(e => e.ChapterId).HasColumnName("chapter_id");
            entity.Property(e => e.QuestionTypeId).HasColumnName("question_type_id");
            entity.Property(e => e.Difficulty).HasColumnName("difficulty");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.Solution).HasColumnName("solution");
            entity.Property(e => e.SolutionSource).HasColumnName("solution_source");
            entity.Property(e => e.ImageUrls)
                .HasColumnType("jsonb")
                .HasColumnName("image_urls")
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<string>())
                .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>>(
                    (a, b) => System.Text.Json.JsonSerializer.Serialize(a, (System.Text.Json.JsonSerializerOptions?)null) == System.Text.Json.JsonSerializer.Serialize(b, (System.Text.Json.JsonSerializerOptions?)null),
                    v => v == null ? 0 : System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null).GetHashCode(),
                    v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null), (System.Text.Json.JsonSerializerOptions?)null) ?? new List<string>()));
            entity.Property(e => e.SourceDocumentId).HasColumnName("source_document_id");
            entity.Property(e => e.SourcePage).HasColumnName("source_page");
            entity.Property(e => e.ReviewStatus)
                .HasDefaultValue("pending_review")
                .HasColumnName("review_status");
            entity.Property(e => e.ReviewedBy).HasColumnName("reviewed_by");
            entity.Property(e => e.ReviewedAt)
                .HasColumnType("timestamp with time zone")
                .HasColumnName("reviewed_at");
            // content_hash do TRIGGER DB tính (sha256 content) khi insert/update content.
            // ValueGeneratedOnAddOrUpdate + không set khi ghi -> EF reload giá trị trigger tính.
            entity.Property(e => e.ContentHash)
                .HasColumnName("content_hash")
                .ValueGeneratedOnAddOrUpdate();
            entity.Property(e => e.EmbeddedHash).HasColumnName("embedded_hash");
            entity.Property(e => e.Embedding)
                .HasColumnType("vector(768)")   // pgvector; gemini-embedding-2, sinh bởi tutora-ai /api/v1/embed
                .HasColumnName("embedding");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Subject).WithMany(p => p.QuestionBanks)
                .HasForeignKey(d => d.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.Gradelevel).WithMany(p => p.QuestionBanks)
                .HasForeignKey(d => d.GradeLevelId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.SourceDocument).WithMany(p => p.Questions)
                .HasForeignKey(d => d.SourceDocumentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(d => d.ChapterNav).WithMany(p => p.Questions)
                .HasForeignKey(d => d.ChapterId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(d => d.QuestionType).WithMany(p => p.Questions)
                .HasForeignKey(d => d.QuestionTypeId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<QuestionVote>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("question_votes_pkey");

            entity.ToTable("question_votes");

            // 1 user 1 vote / câu — chống vote trùng.
            entity.HasIndex(e => new { e.QuestionId, e.UserId }, "question_votes_unique").IsUnique();
            entity.HasIndex(e => e.QuestionId, "idx_question_votes_question");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.QuestionId).HasColumnName("question_id");
            entity.Property(e => e.UserId)
                .HasMaxLength(50)
                .HasColumnName("user_id");
            entity.Property(e => e.Vote).HasColumnName("vote");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Question).WithMany()
                .HasForeignKey(d => d.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SourceDocument>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("source_documents_pkey");

            entity.ToTable("source_documents");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.FileUrl).HasColumnName("file_url");
            entity.Property(e => e.FileName).HasColumnName("file_name");
            entity.Property(e => e.PageCount).HasColumnName("page_count");
            entity.Property(e => e.DefaultSubjectId).HasColumnName("default_subject_id");
            entity.Property(e => e.DefaultGradeLevelId).HasColumnName("default_grade_level_id");
            entity.Property(e => e.Status)
                .HasDefaultValue("pending")
                .HasColumnName("status");
            entity.Property(e => e.QuestionsExtracted)
                .HasDefaultValue(0)
                .HasColumnName("questions_extracted");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.UploadedBy).HasColumnName("uploaded_by");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<Chapter>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("chapters_pkey");
            entity.ToTable("chapters");
            entity.HasIndex(e => new { e.SubjectId, e.GradeLevelId, e.Slug }, "uq_chapters_subject_grade_slug").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SubjectId).HasColumnName("subject_id");
            entity.Property(e => e.GradeLevelId).HasColumnName("grade_level_id");
            entity.Property(e => e.Slug).HasColumnName("slug");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.DisplayOrder).HasColumnName("display_order");
            entity.Property(e => e.IsActive).HasDefaultValue(true).HasColumnName("is_active");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()").HasColumnType("timestamp with time zone").HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("now()").HasColumnType("timestamp with time zone").HasColumnName("updated_at");

            entity.HasOne(d => d.Subject).WithMany().HasForeignKey(d => d.SubjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(d => d.Gradelevel).WithMany().HasForeignKey(d => d.GradeLevelId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<QuestionType>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("question_types_pkey");
            entity.ToTable("question_types");
            entity.HasIndex(e => e.Slug, "question_types_slug_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Slug).HasColumnName("slug");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.DisplayOrder).HasColumnName("display_order");
            entity.Property(e => e.IsActive).HasDefaultValue(true).HasColumnName("is_active");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()").HasColumnType("timestamp with time zone").HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("now()").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
        });

        modelBuilder.Entity<Tutorsubjectgradeprice>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("tutorsubjectgradeprices_pkey");

            entity.ToTable("tutor_subject_grade_prices");

            entity.HasIndex(e => new { e.Tutorid, e.Subjectid, e.Gradelevelid }, "uq_tutorsubjectgradeprices_tutor_subject_grade").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Currency)
                .HasMaxLength(10)
                .HasDefaultValueSql("'VND'::character varying")
                .HasColumnName("currency");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Gradelevelid).HasColumnName("grade_level_id");
            entity.Property(e => e.Isactive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Priceperhour)
                .HasPrecision(12, 2)
                .HasColumnName("price_per_hour");
            entity.Property(e => e.Subjectid).HasColumnName("subject_id");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutor_id");
            entity.Property(e => e.Updatedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.Durationminutespersession)
                .HasDefaultValue(60)
                .HasColumnName("duration_minutes_per_session");
            entity.Property(e => e.Sessionsperweek)
                .HasDefaultValue(1)
                .HasColumnName("sessions_per_week");

            entity.HasOne(d => d.Gradelevel).WithMany(p => p.Tutorsubjectgradeprices)
                .HasForeignKey(d => d.Gradelevelid)
                .HasConstraintName("fk_tutorsubjectgradeprices_gradelevel");

            entity.HasOne(d => d.Subject).WithMany(p => p.Tutorsubjectgradeprices)
                .HasForeignKey(d => d.Subjectid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_tutorsubjectgradeprices_subject");

            entity.HasOne(d => d.Tutor).WithMany(p => p.Tutorsubjectgradeprices)
                .HasForeignKey(d => d.Tutorid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_tutorsubjectgradeprices_tutor");
        });

        modelBuilder.Entity<Tutorpackage>(entity =>
        {
            entity.HasKey(e => e.Packageid).HasName("tutorpackages_pkey");

            entity.ToTable("tutor_packages");

            entity.Property(e => e.Packageid).HasColumnName("package_id");
            entity.Property(e => e.Packagetype).HasColumnName("package_type");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Isactive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutor_id");
            entity.Property(e => e.Updatedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Tutor).WithMany(p => p.Tutorpackages)
                .HasForeignKey(d => d.Tutorid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_tutorpackages_tutor");
        });

        modelBuilder.Entity<Tutorpackagefixedslot>(entity =>
        {
            entity.HasKey(e => e.Fixedslotid).HasName("tutorpackagefixedslots_pkey");

            entity.ToTable("tutor_package_fixed_slots");

            entity.Property(e => e.Fixedslotid).HasColumnName("fixed_slot_id");
            entity.Property(e => e.Packageid).HasColumnName("package_id");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Dayofweek).HasColumnName("day_of_week_id");
            entity.Property(e => e.Endtime).HasColumnName("end_time");
            entity.Property(e => e.Starttime).HasColumnName("start_time");

            entity.HasOne(d => d.Package).WithMany(p => p.Tutorpackagefixedslots)
                .HasForeignKey(d => d.Packageid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_tutorpackagefixedslots_package");

            entity.HasOne(d => d.DayofweekNavigation).WithMany(p => p.Tutorpackagefixedslots)
                .HasForeignKey(d => d.Dayofweek)
                .HasConstraintName("fk_tutorpackagefixedslots_day_of_week");
        });

        modelBuilder.Entity<Tutorsubscription>(entity =>
        {
            entity.HasKey(e => e.Subscriptionid).HasName("tutorsubscriptions_pkey");

            entity.ToTable("tutor_subscriptions");

            entity.Property(e => e.Subscriptionid).HasColumnName("subscription_id");
            entity.Property(e => e.Enddate)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("end_date");
            entity.Property(e => e.Isactive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Packagetype)
                .HasMaxLength(30)
                .HasColumnName("package_type");
            entity.Property(e => e.Paymentstatus)
                .HasMaxLength(20)
                .HasDefaultValueSql("'pending'::character varying")
                .HasColumnName("payment_status");
            entity.Property(e => e.Price)
                .HasPrecision(12, 2)
                .HasColumnName("price");
            entity.Property(e => e.Startdate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("start_date");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutor_id");

            entity.HasOne(d => d.Tutor).WithMany(p => p.Tutorsubscriptions)
                .HasForeignKey(d => d.Tutorid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("tutorsubscriptions_tutorid_fkey");
        });

        modelBuilder.Entity<Topuprequest>(entity =>
        {
            entity.HasKey(e => e.Topuprequestid).HasName("topuprequests_pkey");

            entity.ToTable("topup_requests");

            entity.HasIndex(e => e.Ordercode, "topuprequests_ordercode_key").IsUnique();

            entity.HasIndex(e => new { e.Userid, e.Status }, "idx_topuprequests_userid_status");

            entity.HasIndex(e => new { e.Bookingid, e.Paymentphase, e.Userid, e.Status },
                "idx_topup_requests_booking_phase_user_status");

            entity.Property(e => e.Topuprequestid).HasColumnName("topup_request_id");
            entity.Property(e => e.Bookingid).HasColumnName("booking_id");
            entity.Property(e => e.Paymentphase)
                .HasMaxLength(20)
                .HasColumnName("payment_phase");
            entity.Property(e => e.Ordercode).HasColumnName("order_code");
            entity.Property(e => e.Userid)
                .HasMaxLength(50)
                .HasColumnName("user_id");
            entity.Property(e => e.Amount)
                .HasPrecision(15, 2)
                .HasColumnName("amount");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'pending'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.Paymentlinkid)
                .HasMaxLength(255)
                .HasColumnName("payment_link_id");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Completedat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("completed_at");
            entity.Property(e => e.Expiresat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("expires_at");

            entity.HasOne<Booking>()
                .WithMany()
                .HasForeignKey(e => e.Bookingid)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("topup_requests_booking_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.Topuprequests)
                .HasForeignKey(d => d.Userid)
                .HasConstraintName("topuprequests_userid_fkey");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Userid).HasName("users_pkey");

            entity.ToTable("users");

            entity.HasIndex(e => e.Email, "idx_users_email");

            entity.HasIndex(e => e.Email, "users_email_key").IsUnique();

            entity.HasIndex(e => e.Identitynumber, "users_identitynumber_key").IsUnique();

            entity.HasIndex(e => e.Phone, "users_phone_key").IsUnique();

            entity.HasIndex(e => e.Username, "users_username_key").IsUnique();

            entity.HasIndex(e => e.Zalouserid, "users_zalouserid_key").IsUnique();

            entity.Property(e => e.Userid)
                .HasMaxLength(50)
                .HasColumnName("user_id");
            entity.Property(e => e.Address)
                .HasMaxLength(255)
                .HasColumnName("address");
            entity.Property(e => e.Avatarurl)
                .HasMaxLength(1000)
                .HasColumnName("avatar_url");
            entity.Property(e => e.Birthdate).HasColumnName("birth_date");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Ekycrawdata).HasColumnName("ekyc_raw_data");
            entity.Property(e => e.Email)
                .HasMaxLength(100)
                .IsRequired(false)
                .HasColumnName("email");
            entity.Property(e => e.Fullname)
                .HasMaxLength(100)
                .HasColumnName("full_name");
            entity.Property(e => e.Gender)
                .HasColumnName("gender")
                .HasConversion<short>();
            entity.Property(e => e.Googlecalendartoken).HasColumnName("google_calendar_token");
            entity.Property(e => e.Fcmtoken)
                .HasMaxLength(500)
                .HasColumnName("fcm_token");
            entity.Property(e => e.Idcardbackurl)
                .HasMaxLength(1000)
                .HasColumnName("id_card_back_url");
            entity.Property(e => e.Idcardfronturl)
                .HasMaxLength(1000)
                .HasColumnName("id_card_front_url");
            entity.Property(e => e.Identitynumber)
                .HasMaxLength(50)
                .HasColumnName("identity_number");
            entity.Property(e => e.Isemailverified)
                .HasDefaultValue(false)
                .HasColumnName("is_email_verified");
            entity.Property(e => e.Isidentityverified)
                .HasDefaultValue(false)
                .HasColumnName("is_identity_verified");
            entity.Property(e => e.Isphoneverified)
                .HasDefaultValue(false)
                .HasColumnName("is_phone_verified");
            entity.Property(e => e.Lastloginat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("last_login_at");
            entity.Property(e => e.Lastseenat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("last_seen_at");
            entity.Property(e => e.Password)
                .HasMaxLength(255)
                .HasColumnName("password");
            entity.Property(e => e.Phone)
                .HasMaxLength(20)
                .HasColumnName("phone");
            entity.Property(e => e.Primaryrole)
                .HasMaxLength(20)
                .HasColumnName("primary_role");
            entity.Property(e => e.Status)
                .HasDefaultValue(1)
                .HasColumnName("status");
            entity.Property(e => e.Username)
                .HasMaxLength(50)
                .HasColumnName("username");
            entity.Property(e => e.Zabornotifyenabled)
                .HasDefaultValue(true)
                .HasColumnName("zabo_notify_enabled");
            entity.Property(e => e.Zalouserid)
                .HasMaxLength(100)
                .HasColumnName("zalo_user_id");
            entity.Property(e => e.Parentcode)
                .HasMaxLength(10)
                .HasColumnName("parent_code");
            entity.Property(e => e.Parentcodeexpiresat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("parent_code_expires_at");
            entity.Property(e => e.Hascompletedtour)
                .HasDefaultValue(false)
                .HasColumnName("has_completed_tour");
            entity.Property(e => e.Isdeactivated)
                .HasDefaultValue(false)
                .HasColumnName("is_deactivated");
            entity.Property(e => e.Deactivatedat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("deactivated_at");
            entity.Property(e => e.AiCreditsBalance)
                .HasDefaultValue(0)
                .HasColumnName("ai_credits_balance");

            entity.HasIndex(e => e.Parentcode, "users_parentcode_key")
                .IsUnique()
                .HasFilter("parent_code IS NOT NULL");
        });

        modelBuilder.Entity<AiCreditTransaction>(entity =>
        {
            entity.HasKey(e => e.Transactionid).HasName("ai_credit_transactions_pkey");

            entity.ToTable("ai_credit_transactions");

            entity.HasIndex(e => new { e.Userid, e.Createdat }, "idx_ai_credit_transactions_user_createdat");

            entity.Property(e => e.Transactionid).HasColumnName("transaction_id");
            entity.Property(e => e.Userid)
                .HasMaxLength(50)
                .HasColumnName("user_id");
            entity.Property(e => e.Amount).HasColumnName("amount");
            entity.Property(e => e.Balanceafter).HasColumnName("balance_after");
            entity.Property(e => e.Source)
                .HasMaxLength(30)
                .HasColumnName("source");
            entity.Property(e => e.Referenceid)
                .HasMaxLength(50)
                .HasColumnName("reference_id");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");

            entity.HasOne(d => d.User).WithMany(p => p.AiCreditTransactions)
                .HasForeignKey(d => d.Userid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("ai_credit_transactions_userid_fkey");
        });

        modelBuilder.Entity<AiCreditPackage>(entity =>
        {
            entity.HasKey(e => e.Packageid).HasName("ai_credit_packages_pkey");

            entity.ToTable("ai_credit_packages");

            entity.Property(e => e.Packageid).HasColumnName("package_id");
            entity.Property(e => e.Code)
                .HasMaxLength(30)
                .HasColumnName("code");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.Creditamount).HasColumnName("credit_amount");
            entity.Property(e => e.Price)
                .HasColumnType("numeric(12,2)")
                .HasColumnName("price");
            entity.Property(e => e.Currency)
                .HasMaxLength(10)
                .HasDefaultValue("VND")
                .HasColumnName("currency");
            entity.Property(e => e.Ispurchasable)
                .HasDefaultValue(true)
                .HasColumnName("is_purchasable");
            entity.Property(e => e.Isactive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Sortorder)
                .HasDefaultValue(0)
                .HasColumnName("sort_order");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Iconurl)
                .HasMaxLength(1000)
                .HasColumnName("icon_url");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Updatedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<Userwarning>(entity =>
        {
            entity.HasKey(e => e.Warningid).HasName("userwarnings_pkey");

            entity.ToTable("user_warnings");

            entity.HasIndex(e => e.Userid, "idx_userwarnings_user");

            entity.Property(e => e.Warningid).HasColumnName("warning_id");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Issuedby)
                .HasMaxLength(50)
                .HasColumnName("issued_by");
            entity.Property(e => e.Reason).HasColumnName("reason");
            entity.Property(e => e.Relatedbookingid).HasColumnName("related_booking_id");
            entity.Property(e => e.Userid)
                .HasMaxLength(50)
                .HasColumnName("user_id");
            entity.Property(e => e.Warninglevel).HasColumnName("warning_level");

            entity.HasOne(d => d.IssuedbyNavigation).WithMany(p => p.UserwarningIssuedbyNavigations)
                .HasForeignKey(d => d.Issuedby)
                .HasConstraintName("userwarnings_issuedby_fkey");

            entity.HasOne(d => d.Relatedbooking).WithMany(p => p.Userwarnings)
                .HasForeignKey(d => d.Relatedbookingid)
                .HasConstraintName("userwarnings_relatedbookingid_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.UserwarningUsers)
                .HasForeignKey(d => d.Userid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("userwarnings_userid_fkey");
        });

        modelBuilder.Entity<Wallet>(entity =>
        {
            entity.HasKey(e => e.Walletid).HasName("wallets_pkey");

            entity.ToTable("wallets");

            entity.HasIndex(e => e.Userid, "wallets_userid_key").IsUnique();

            entity.Property(e => e.Walletid).HasColumnName("wallet_id");
            entity.Property(e => e.Balance)
                .HasPrecision(15, 2)
                .HasDefaultValueSql("0")
                .HasColumnName("balance");
            entity.Property(e => e.Frozenbalance)
                .HasPrecision(15, 2)
                .HasDefaultValueSql("0")
                .HasColumnName("frozen_balance");
            entity.Property(e => e.Lastupdated)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("last_updated");
            entity.Property(e => e.Userid)
                .HasMaxLength(50)
                .HasColumnName("user_id");

            entity.HasOne(d => d.User).WithOne(p => p.Wallet)
                .HasForeignKey<Wallet>(d => d.Userid)
                .HasConstraintName("wallets_userid_fkey");
        });

        modelBuilder.Entity<PaymentRequest>(entity =>
        {
            entity.HasKey(e => e.Paymentrequestid).HasName("payment_requests_pkey");

            entity.ToTable("payment_requests");

            entity.HasIndex(e => new { e.Provider, e.Paymentlinkid }, "uq_payment_requests_provider_link")
                .IsUnique()
                .HasFilter("payment_link_id IS NOT NULL");
            entity.HasIndex(e => new { e.Provider, e.Ordercode }, "uq_payment_requests_provider_order")
                .IsUnique()
                .HasFilter("order_code IS NOT NULL");
            entity.HasIndex(e => new { e.Bookingid, e.Phase }, "uq_payment_requests_active_booking_phase")
                .IsUnique()
                .HasFilter("phase IN ('deposit', 'remaining') AND status IN ('PENDING', 'PROCESSING', 'REQUIRES_REVIEW', 'UNKNOWN')");
            entity.HasIndex(e => new { e.Bookingid, e.Createdat }, "idx_payment_requests_booking_created");
            entity.HasIndex(e => e.Userid, "idx_payment_requests_user");
            entity.HasIndex(e => e.Status, "idx_payment_requests_status");

            entity.Property(e => e.Paymentrequestid).HasColumnName("payment_request_id");
            entity.Property(e => e.Bookingid).HasColumnName("booking_id");
            entity.Property(e => e.Userid).HasMaxLength(50).HasColumnName("user_id");
            entity.Property(e => e.Provider).HasMaxLength(20).HasColumnName("provider");
            entity.Property(e => e.Phase).HasMaxLength(20).HasColumnName("phase");
            entity.Property(e => e.Ordercode).HasColumnName("order_code");
            entity.Property(e => e.Paymentlinkid).HasMaxLength(255).HasColumnName("payment_link_id");
            entity.Property(e => e.Amount).HasPrecision(15, 2).HasColumnName("amount");
            entity.Property(e => e.Currency).HasMaxLength(10).HasColumnName("currency");
            entity.Property(e => e.Status).HasMaxLength(30).HasColumnName("status");
            entity.Property(e => e.Destinationbankbin).HasMaxLength(20).HasColumnName("destination_bank_bin");
            entity.Property(e => e.Destinationbankname).HasMaxLength(100).HasColumnName("destination_bank_name");
            entity.Property(e => e.Displayaccountnumber).HasMaxLength(50).HasColumnName("display_account_number");
            entity.Property(e => e.Displayaccountname).HasMaxLength(200).HasColumnName("display_account_name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Checkouturl).HasColumnName("checkout_url");
            entity.Property(e => e.Qrcode).HasColumnName("qr_code");
            entity.Property(e => e.Expiresat).HasColumnType("timestamp without time zone").HasColumnName("expires_at");
            entity.Property(e => e.Providerpayload).HasColumnType("jsonb").HasColumnName("provider_payload");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Updatedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.AiCreditPackageid).HasColumnName("ai_credit_package_id");
            entity.Property(e => e.AiCreditUserid).HasMaxLength(50).HasColumnName("ai_credit_user_id");

            entity.HasOne(d => d.Booking).WithMany(p => p.Paymentrequests)
                .HasForeignKey(d => d.Bookingid)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("payment_requests_booking_id_fkey");

            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.Userid)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("payment_requests_user_id_fkey");
        });

        modelBuilder.Entity<PaymentTransaction>(entity =>
        {
            entity.HasKey(e => e.Paymenttransactionid).HasName("payment_transactions_pkey");

            entity.ToTable("payment_transactions");

            entity.HasIndex(e => new { e.Paymentmethod, e.Providertransactionid }, "uq_payment_transactions_payment_method_provider_transaction_id")
                .IsUnique()
                .HasFilter("provider_transaction_id IS NOT NULL");
            entity.HasIndex(e => e.Userid, "idx_payment_transactions_user_id");
            entity.HasIndex(e => e.Ordercode, "idx_payment_transactions_order_code");
            entity.HasIndex(e => e.Bookingid, "idx_payment_transactions_booking_id");
            entity.HasIndex(e => e.Withdrawalid, "idx_payment_transactions_withdrawal_id");
            entity.HasIndex(e => e.Withdrawalid, "uq_payment_transactions_withdrawal_payout")
                .IsUnique()
                .HasFilter("withdrawal_id IS NOT NULL AND purpose = 'Withdrawal' AND status = 'Succeeded'");
            entity.HasIndex(e => e.Paymentrequestid, "idx_payment_transactions_payment_request");
            entity.HasIndex(e => e.Paidat, "idx_payment_transactions_paid_at");
            entity.HasIndex(e => new { e.Paymentmethod, e.Capturefingerprint }, "uq_payment_transactions_payment_method_capture_fingerprint")
                .IsUnique()
                .HasFilter("provider_transaction_id IS NULL AND capture_fingerprint IS NOT NULL");

            entity.Property(e => e.Paymenttransactionid).HasColumnName("payment_transaction_id");
            entity.Property(e => e.Userid)
                .HasMaxLength(50)
                .HasColumnName("user_id");
            entity.Property(e => e.Paymentmethod)
                .HasMaxLength(20)
                .HasColumnName("payment_method");
            entity.Property(e => e.Direction)
                .HasMaxLength(20)
                .HasColumnName("direction");
            entity.Property(e => e.Purpose)
                .HasMaxLength(50)
                .HasColumnName("purpose");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'Succeeded'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.Amount)
                .HasPrecision(15, 2)
                .HasColumnName("amount");
            entity.Property(e => e.Currency)
                .HasMaxLength(10)
                .HasDefaultValueSql("'VND'::character varying")
                .HasColumnName("currency");
            entity.Property(e => e.Ordercode).HasColumnName("order_code");
            entity.Property(e => e.Providertransactionid).HasColumnName("provider_transaction_id");
            entity.Property(e => e.Paymentlinkid)
                .HasMaxLength(255)
                .HasColumnName("payment_link_id");
            entity.Property(e => e.Paymentrequestid).HasColumnName("payment_request_id");
            entity.Property(e => e.Bookingid).HasColumnName("booking_id");
            entity.Property(e => e.Withdrawalid).HasColumnName("withdrawal_id");
            entity.Property(e => e.AiCreditPackageid).HasColumnName("ai_credit_package_id");
            entity.Property(e => e.AiCreditUserid)
                .HasMaxLength(50)
                .HasColumnName("ai_credit_user_id");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Paidat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("paid_at");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Processedby)
                .HasMaxLength(50)
                .HasColumnName("processed_by");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.Capturesource)
                .HasMaxLength(20)
                .HasDefaultValueSql("'Legacy'::character varying")
                .HasColumnName("capture_source");
            entity.Property(e => e.Reconciliationstatus)
                .HasMaxLength(30)
                .HasDefaultValueSql("'Matched'::character varying")
                .HasColumnName("reconciliation_status");
            entity.Property(e => e.Capturefingerprint)
                .HasMaxLength(64)
                .HasColumnName("capture_fingerprint");
            entity.Property(e => e.Webhookcode)
                .HasMaxLength(50)
                .HasColumnName("webhook_code");
            entity.Property(e => e.Webhookdesc).HasColumnName("webhook_desc");
            entity.Property(e => e.Webhooksuccess).HasColumnName("webhook_success");
            entity.Property(e => e.Providercode)
                .HasMaxLength(50)
                .HasColumnName("provider_code");
            entity.Property(e => e.Providerdesc).HasColumnName("provider_desc");
            entity.Property(e => e.Sourceaccountbankid)
                .HasMaxLength(50)
                .HasColumnName("source_account_bank_id");
            entity.Property(e => e.Sourceaccountbankname)
                .HasMaxLength(100)
                .HasColumnName("source_account_bank_name");
            entity.Property(e => e.Sourceaccountnumber)
                .HasMaxLength(50)
                .HasColumnName("source_account_number");
            entity.Property(e => e.Sourceaccountname)
                .HasMaxLength(200)
                .HasColumnName("source_account_name");
            entity.Property(e => e.Destinationaccountbankbin)
                .HasMaxLength(50)
                .HasColumnName("destination_account_bank_bin");
            entity.Property(e => e.Destinationaccountbankname)
                .HasMaxLength(100)
                .HasColumnName("destination_account_bank_name");
            entity.Property(e => e.Destinationaccountnumber)
                .HasMaxLength(50)
                .HasColumnName("destination_account_number");
            entity.Property(e => e.Destinationaccountname)
                .HasMaxLength(200)
                .HasColumnName("destination_account_name");
            entity.Property(e => e.Destinationvirtualaccountnumber)
                .HasMaxLength(50)
                .HasColumnName("destination_virtual_account_number");
            entity.Property(e => e.Destinationvirtualaccountname)
                .HasMaxLength(200)
                .HasColumnName("destination_virtual_account_name");
            entity.Property(e => e.Providerpayload)
                .HasColumnType("jsonb")
                .HasColumnName("provider_payload");
            entity.Property(e => e.Webhookpayload)
                .HasColumnType("text")
                .HasColumnName("webhook_payload");
            entity.Property(e => e.Proofimagepath)
                .HasColumnType("text")
                .HasColumnName("proof_image_path");

            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.Userid)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("payment_transactions_user_id_fkey");

            entity.HasOne(d => d.ProcessedbyNavigation).WithMany()
                .HasForeignKey(d => d.Processedby)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("payment_transactions_processed_by_fkey");

            entity.HasOne(d => d.Booking).WithMany()
                .HasForeignKey(d => d.Bookingid)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("payment_transactions_booking_id_fkey");

            entity.HasOne(d => d.Paymentrequest).WithMany(p => p.Paymenttransactions)
                .HasForeignKey(d => d.Paymentrequestid)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("payment_transactions_payment_request_id_fkey");

            entity.HasOne(d => d.Withdrawal).WithMany()
                .HasForeignKey(d => d.Withdrawalid)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("payment_transactions_withdrawal_id_fkey");
        });
        modelBuilder.Entity<Wallettransaction>(entity =>
        {
            entity.HasKey(e => e.Transactionid).HasName("wallettransactions_pkey");

            entity.ToTable("wallet_transactions");

            entity.Property(e => e.Transactionid).HasColumnName("transaction_id");
            entity.Property(e => e.Amount)
                .HasPrecision(15, 2)
                .HasColumnName("amount");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Ordercode).HasColumnName("order_code");
            entity.Property(e => e.Referenceid).HasColumnName("reference_id");
            entity.Property(e => e.Referencetable)
                .HasMaxLength(50)
                .HasColumnName("reference_table");
            entity.Property(e => e.Transactiontype)
                .HasMaxLength(50)
                .HasColumnName("transaction_type");
            entity.Property(e => e.Walletid).HasColumnName("wallet_id");

            entity.HasOne(d => d.Wallet).WithMany(p => p.Wallettransactions)
                .HasForeignKey(d => d.Walletid)
                .HasConstraintName("wallettransactions_walletid_fkey");
        });

        modelBuilder.Entity<Withdrawalrequest>(entity =>
        {
            entity.HasKey(e => e.Withdrawalid).HasName("withdrawalrequests_pkey");

            entity.ToTable("withdrawal_requests");

            entity.HasIndex(
                    e => new { e.Status, e.Claimedby, e.Claimedat },
                    "idx_withdrawal_requests_status_claimed_by");

            entity.Property(e => e.Withdrawalid).HasColumnName("withdrawal_id");
            entity.Property(e => e.Accountholdername)
                .HasMaxLength(100)
                .HasColumnName("account_holder_name");
            entity.Property(e => e.Accountnumber)
                .HasMaxLength(50)
                .HasColumnName("account_number");
            entity.Property(e => e.Amount)
                .HasPrecision(15, 2)
                .HasColumnName("amount");
            entity.Property(e => e.Bankname)
                .HasMaxLength(100)
                .HasColumnName("bank_name");
            entity.Property(e => e.Processedat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("processed_at");
            entity.Property(e => e.Requestedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("requested_at");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasColumnName("status");
            entity.Property(e => e.Userid)
                .HasMaxLength(50)
                .HasColumnName("user_id");
            entity.Property(e => e.Walletid).HasColumnName("wallet_id");

            entity.Property(e => e.Completionnote).HasColumnName("completion_note");
            entity.Property(e => e.Claimedby)
                .HasMaxLength(50)
                .HasColumnName("claimed_by");
            entity.Property(e => e.Claimedat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("claimed_at");
            entity.Property(e => e.Rejectionreason).HasColumnName("rejection_reason");

            // Decision tracking fields
            entity.Property(e => e.Decision)
                .HasMaxLength(50)
                .HasColumnName("decision");
            entity.Property(e => e.Processedby)
                .HasMaxLength(50)
                .HasColumnName("processed_by");

            entity.HasOne(d => d.User).WithMany(p => p.Withdrawalrequests)
                .HasForeignKey(d => d.Userid)
                .HasConstraintName("withdrawalrequests_userid_fkey");

            entity.HasOne<User>().WithMany()
                .HasForeignKey(d => d.Claimedby)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("withdrawal_requests_claimed_by_fkey");

            entity.HasOne(d => d.Wallet).WithMany(p => p.Withdrawalrequests)
                .HasForeignKey(d => d.Walletid)
                .HasConstraintName("withdrawal_requests_walletid_fkey");
        });

        modelBuilder.Entity<Systemalert>(entity =>
        {
            entity.HasKey(e => e.Alertid).HasName("systemalerts_pkey");

            entity.ToTable("system_alerts");

            entity.Property(e => e.Alertid).HasColumnName("alert_id");
            entity.Property(e => e.Type)
                .HasMaxLength(50)
                .HasColumnName("type");
            entity.Property(e => e.Severity)
                .HasMaxLength(20)
                .HasColumnName("severity");
            entity.Property(e => e.Message)
                .HasColumnName("message");
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .HasColumnName("metadata");
            entity.Property(e => e.Resolved)
                .HasDefaultValue(false)
                .HasColumnName("resolved");
            entity.Property(e => e.Resolvedat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("resolved_at");
            entity.Property(e => e.Resolvedby)
                .HasMaxLength(50)
                .HasColumnName("resolved_by");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("refreshtokens_pkey");

            entity.ToTable("refresh_tokens");

            entity.HasIndex(e => e.Tokenhash).IsUnique().HasDatabaseName("idx_refreshtokens_tokenhash");
            entity.HasIndex(e => e.Userid).HasDatabaseName("idx_refreshtokens_userid");
            entity.HasIndex(e => e.Tokenfamily).HasDatabaseName("idx_refreshtokens_tokenfamily");
            entity.HasIndex(e => e.Expiresat).HasDatabaseName("idx_refreshtokens_expiresat");

            entity.Property(e => e.Id).HasMaxLength(50).HasColumnName("id");
            entity.Property(e => e.Tokenhash).HasMaxLength(128).HasColumnName("token_hash");
            entity.Property(e => e.Userid).HasMaxLength(50).HasColumnName("user_id");
            entity.Property(e => e.Tokenfamily).HasMaxLength(50).HasColumnName("token_family");
            entity.Property(e => e.Expiresat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("expires_at");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Revokedat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("revoked_at");
            entity.Property(e => e.Replacedbytokenhash).HasMaxLength(128).HasColumnName("replaced_by_token_hash");

            entity.HasOne(d => d.User).WithMany(p => p.RefreshTokens)
                .HasForeignKey(d => d.Userid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_refreshtokens_user");
        });

        modelBuilder.Entity<StaffPermission>(entity =>
        {
            entity.HasKey(e => new { e.Userid, e.PermissionKey }).HasName("staff_permissions_pkey");

            entity.ToTable("staff_permissions");

            entity.Property(e => e.Userid).HasMaxLength(50).HasColumnName("user_id");
            entity.Property(e => e.PermissionKey).HasMaxLength(100).HasColumnName("permission_key");
            entity.Property(e => e.GrantedBy).HasMaxLength(50).HasColumnName("granted_by");
            entity.Property(e => e.GrantedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("granted_at");

            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.Userid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("staff_permissions_userid_fkey");
        });

        OnModelCreatingPartial(modelBuilder);

        // ── UTC DateTime Convention ───────────────────────────────────────────
        // Vì DB dùng `timestamp without time zone` + EnableLegacyTimestampBehavior,
        // EF Core đọc DateTime ra với Kind = Unspecified.
        // Convention này đảm bảo tất cả DateTime/DateTime? từ DB đều có Kind = Utc
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
                        v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
                        v => DateTime.SpecifyKind(v, DateTimeKind.Utc)
                    ));
                }
                else if (property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>(
                        v => v.HasValue
                            ? (v.Value.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc))
                            : v,
                        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v
                    ));
                }
            }
        }
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
