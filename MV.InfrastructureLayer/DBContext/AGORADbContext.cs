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

    public virtual DbSet<Booking> Bookings { get; set; }

    public virtual DbSet<BankChangeLog> BankChangeLogs { get; set; }

    public virtual DbSet<FraudLog> Fraudlogs { get; set; }

    public virtual DbSet<LoginHistory> Loginhistories { get; set; }

    public virtual DbSet<Chatchannel> Chatchannels { get; set; }

    public virtual DbSet<Chatmessage> Chatmessages { get; set; }

    public virtual DbSet<ChatSession> ChatSessions { get; set; }

    public virtual DbSet<ChatHistory> ChatHistories { get; set; }

    public virtual DbSet<Class> Classes { get; set; }

    public virtual DbSet<Dispute> Disputes { get; set; }

    public virtual DbSet<DisputeEvidence> DisputeEvidences { get; set; }

    public virtual DbSet<Feedback> Feedbacks { get; set; }

    public virtual DbSet<Handoversummary> Handoversummaries { get; set; }

    public virtual DbSet<Learningmaterial> Learningmaterials { get; set; }

    public virtual DbSet<ClassSession> ClassSessions { get; set; }

    public virtual DbSet<ClassSessionReport> ClassSessionReports { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<QuestionBank> QuestionBanks { get; set; }

    public virtual DbSet<AiCreditTransaction> AiCreditTransactions { get; set; }

    public virtual DbSet<Profilesuspension> Profilesuspensions { get; set; }

    public virtual DbSet<Promotion> Promotions { get; set; }

    public virtual DbSet<Gradelevel> Gradelevels { get; set; }



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

        modelBuilder.Entity<BankChangeLog>(entity =>
        {
            entity.HasKey(e => e.Logid).HasName("bank_change_logs_pkey");

            entity.ToTable("bank_change_logs");

            entity.Property(e => e.Logid).HasColumnName("log_id");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutor_id");
            entity.Property(e => e.Oldbankname)
                .HasMaxLength(100)
                .HasColumnName("old_bank_name");
            entity.Property(e => e.Oldbankaccountnumber)
                .HasMaxLength(50)
                .HasColumnName("old_bank_account_number");
            entity.Property(e => e.Oldbankaccountname)
                .HasMaxLength(200)
                .HasColumnName("old_bank_account_name");
            entity.Property(e => e.Newbankname)
                .HasMaxLength(100)
                .HasColumnName("new_bank_name");
            entity.Property(e => e.Newbankaccountnumber)
                .HasMaxLength(50)
                .HasColumnName("new_bank_account_number");
            entity.Property(e => e.Newbankaccountname)
                .HasMaxLength(200)
                .HasColumnName("new_bank_account_name");
            entity.Property(e => e.Changedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("changed_at");
            entity.Property(e => e.Ipaddress)
                .HasMaxLength(45)
                .HasColumnName("ip_address");
            entity.Property(e => e.Useragent).HasColumnName("user_agent");
            entity.Property(e => e.Reason)
                .HasMaxLength(500)
                .HasColumnName("reason");

            entity.HasOne(d => d.Tutor).WithMany()
                .HasForeignKey(d => d.Tutorid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_bank_change_logs_tutor");
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

            entity.HasIndex(e => e.Paymentcode, "bookings_paymentcode_key").IsUnique();

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
            entity.Property(e => e.Paymentcode)
                .HasMaxLength(50)
                .HasColumnName("payment_code");
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
                .HasColumnName("status");
            entity.Property(e => e.Studentid)
                .HasMaxLength(50)
                .HasColumnName("student_id");
            entity.Property(e => e.Createdbyrole)
                .HasMaxLength(20)
                .HasColumnName("created_by_role");
            entity.Property(e => e.Responsedeadline)
                .HasColumnName("response_deadline");
            entity.Property(e => e.Payosbin).HasMaxLength(20).HasColumnName("payos_bin");
            entity.Property(e => e.Payosaccountnumber).HasMaxLength(50).HasColumnName("payos_account_number");
            entity.Property(e => e.Payosaccountname).HasMaxLength(200).HasColumnName("payos_account_name");
            entity.Property(e => e.Payosdescription).HasMaxLength(100).HasColumnName("payos_description");
            entity.Property(e => e.Payoscheckouturl).HasColumnName("payos_checkout_url");
            entity.Property(e => e.Payosqrcode).HasColumnName("payos_qr_code");
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
            entity.Property(e => e.Dayofweek).HasColumnName("day_of_week");
            entity.Property(e => e.Endtime).HasColumnName("end_time");
            entity.Property(e => e.Starttime).HasColumnName("start_time");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutor_id");

            entity.HasOne(d => d.Tutor).WithMany(p => p.Tutoravailabilities)
                .HasForeignKey(d => d.Tutorid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("tutoravailability_tutorid_fkey");
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

            entity.HasIndex(e => new { e.Isbankverified, e.Bankverifiedat }, "idx_tutorprofiles_bankverified")
                .HasFilter("is_bank_verified = true");

            entity.HasIndex(e => e.Bankverifycode, "idx_tutorprofiles_bankverifycode")
                .HasFilter("bank_verify_code IS NOT NULL");

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
            entity.Property(e => e.Bankname)
                .HasMaxLength(100)
                .HasColumnName("bank_name");
            entity.Property(e => e.Bankaccountnumber)
                .HasMaxLength(50)
                .HasColumnName("bank_account_number");
            entity.Property(e => e.Bankaccountname)
                .HasMaxLength(100)
                .HasColumnName("bank_account_name");
            entity.Property(e => e.Isbankverified)
                .HasDefaultValue(false)
                .HasColumnName("is_bank_verified");
            entity.Property(e => e.Bankchangedat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("bank_changed_at");
            entity.Property(e => e.Bankverifycode)
                .HasMaxLength(50)
                .HasColumnName("bank_verify_code");
            entity.Property(e => e.Bankverifystatus)
                .HasMaxLength(30)
                .HasColumnName("bank_verify_status");
            entity.Property(e => e.Bankverifyrequested)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("bank_verify_requested");
            entity.Property(e => e.Bankverifiedat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("bank_verified_at");
            entity.Property(e => e.Bankverifyattempts)
                .HasDefaultValue(0)
                .HasColumnName("bank_verify_attempts");

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
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
        });

        modelBuilder.Entity<QuestionBank>(entity =>
        {
            entity.HasKey(e => e.Questionid).HasName("question_bank_pkey");

            entity.ToTable("question_bank");

            entity.HasIndex(e => new { e.Subjectid, e.Gradelevelid }, "idx_question_bank_subject_grade")
                .HasFilter("(is_active = true)");

            entity.Property(e => e.Questionid).HasColumnName("question_id");
            entity.Property(e => e.Subjectid).HasColumnName("subject_id");
            entity.Property(e => e.Gradelevelid).HasColumnName("grade_level_id");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.Answer).HasColumnName("answer");
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .HasColumnName("metadata");
            entity.Property(e => e.Embedding)
                .HasColumnType("jsonb")
                .HasColumnName("embedding");
            entity.Property(e => e.Isactive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Updatedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Subject).WithMany(p => p.QuestionBanks)
                .HasForeignKey(d => d.Subjectid)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("question_bank_subjectid_fkey");

            entity.HasOne(d => d.Gradelevel).WithMany(p => p.QuestionBanks)
                .HasForeignKey(d => d.Gradelevelid)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("question_bank_gradelevelid_fkey");
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
            entity.Property(e => e.Dayofweek).HasColumnName("day_of_week");
            entity.Property(e => e.Endtime).HasColumnName("end_time");
            entity.Property(e => e.Starttime).HasColumnName("start_time");

            entity.HasOne(d => d.Package).WithMany(p => p.Tutorpackagefixedslots)
                .HasForeignKey(d => d.Packageid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_tutorpackagefixedslots_package");
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

            entity.Property(e => e.Topuprequestid).HasColumnName("topup_request_id");
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

            // PayOS tracking fields
            entity.Property(e => e.Payostransactionid)
                .HasMaxLength(255)
                .HasColumnName("payos_transaction_id");
            entity.Property(e => e.Payosstatus)
                .HasMaxLength(50)
                .HasColumnName("payos_status");
            entity.Property(e => e.Payosresponsecode)
                .HasMaxLength(50)
                .HasColumnName("payos_response_code");
            entity.Property(e => e.Payoserror)
                .HasColumnName("payos_error");
            entity.Property(e => e.Retrycount)
                .HasDefaultValue(0)
                .HasColumnName("retry_count");
            entity.Property(e => e.Lastretryat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("last_retry_at");

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
