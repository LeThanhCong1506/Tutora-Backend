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

    public virtual DbSet<Class> Classes { get; set; }

    public virtual DbSet<Dispute> Disputes { get; set; }

    public virtual DbSet<Feedback> Feedbacks { get; set; }

    public virtual DbSet<Handoversummary> Handoversummaries { get; set; }

    public virtual DbSet<Learningmaterial> Learningmaterials { get; set; }

    public virtual DbSet<Lesson> Lessons { get; set; }

    public virtual DbSet<Lessonreport> Lessonreports { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pgcrypto");

        modelBuilder.Entity<Studentprofile>().HasQueryFilter(e => EF.Property<DateTime?>(e, "Deletedat") == null);
        modelBuilder.Entity<Tutorprofile>().HasQueryFilter(e => EF.Property<DateTime?>(e, "Deletedat") == null);

        modelBuilder.Entity<BankChangeLog>(entity =>
        {
            entity.HasKey(e => e.Logid).HasName("bank_change_logs_pkey");

            entity.ToTable("bank_change_logs");

            entity.Property(e => e.Logid).HasColumnName("logid");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutorid");
            entity.Property(e => e.Oldbankname)
                .HasMaxLength(100)
                .HasColumnName("oldbankname");
            entity.Property(e => e.Oldbankaccountnumber)
                .HasMaxLength(50)
                .HasColumnName("oldbankaccountnumber");
            entity.Property(e => e.Oldbankaccountname)
                .HasMaxLength(200)
                .HasColumnName("oldbankaccountname");
            entity.Property(e => e.Newbankname)
                .HasMaxLength(100)
                .HasColumnName("newbankname");
            entity.Property(e => e.Newbankaccountnumber)
                .HasMaxLength(50)
                .HasColumnName("newbankaccountnumber");
            entity.Property(e => e.Newbankaccountname)
                .HasMaxLength(200)
                .HasColumnName("newbankaccountname");
            entity.Property(e => e.Changedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("changedat");
            entity.Property(e => e.Ipaddress)
                .HasMaxLength(45)
                .HasColumnName("ipaddress");
            entity.Property(e => e.Useragent).HasColumnName("useragent");
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

            entity.Property(e => e.Logid).HasColumnName("logid");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutorid");
            entity.Property(e => e.Withdrawalrequestid).HasColumnName("withdrawalrequestid");
            entity.Property(e => e.Rulename)
                .HasMaxLength(100)
                .HasColumnName("rulename");
            entity.Property(e => e.Passed).HasColumnName("passed");
            entity.Property(e => e.Isflagged).HasColumnName("isflagged");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .HasColumnName("metadata");
            entity.Property(e => e.Checkedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("checkedat");

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

            entity.Property(e => e.Logid).HasColumnName("logid");
            entity.Property(e => e.Userid)
                .HasMaxLength(50)
                .HasColumnName("userid");
            entity.Property(e => e.Ipaddress)
                .HasMaxLength(45)
                .HasColumnName("ipaddress");
            entity.Property(e => e.Useragent).HasColumnName("useragent");
            entity.Property(e => e.Loggedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("loggedat");

            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.Userid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_login_history_user");
        });

        modelBuilder.Entity<WithdrawalScore>(entity =>
        {
            entity.HasKey(e => e.Scoreid).HasName("withdrawal_scores_pkey");

            entity.ToTable("withdrawal_scores");

            entity.Property(e => e.Scoreid).HasColumnName("scoreid");
            entity.Property(e => e.Withdrawalrequestid).HasColumnName("withdrawalrequestid");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutorid");
            entity.Property(e => e.Basescore).HasColumnName("basescore");
            entity.Property(e => e.Positivefactors)
                .HasColumnType("jsonb")
                .HasColumnName("positivefactors");
            entity.Property(e => e.Negativefactors)
                .HasColumnType("jsonb")
                .HasColumnName("negativefactors");
            entity.Property(e => e.Fraudflags)
                .HasColumnType("jsonb")
                .HasColumnName("fraudflags");
            entity.Property(e => e.Totalscore).HasColumnName("totalscore");
            entity.Property(e => e.Decision)
                .HasMaxLength(50)
                .HasColumnName("decision");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");

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

            entity.Property(e => e.Bookingid).HasColumnName("bookingid");
            entity.Property(e => e.Cancellationreason).HasColumnName("cancellationreason");
            entity.Property(e => e.Cancelledat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("cancelledat");
            entity.Property(e => e.Cancelledby)
                .HasMaxLength(50)
                .HasColumnName("cancelledby");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
            entity.Property(e => e.Depositamount)
                .HasPrecision(12, 2)
                .HasColumnName("depositamount");
            entity.Property(e => e.Depositpaidat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("depositpaidat");
            entity.Property(e => e.Discountapplied)
                .HasPrecision(18, 2)
                .HasColumnName("discountapplied");

            entity.Property(e => e.Escrowstatus)
                .HasMaxLength(20)
                .HasDefaultValueSql("'held'::character varying")
                .HasColumnName("escrowstatus");
            entity.Property(e => e.Finalprice)
                .HasPrecision(12, 2)
                .HasColumnName("finalprice");
            entity.Property(e => e.Graceperiodends)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("graceperiodends");
            entity.Property(e => e.Locationcity)
                .HasMaxLength(50)
                .HasColumnName("locationcity");
            entity.Property(e => e.Locationdetail)
                .HasMaxLength(255)
                .HasColumnName("locationdetail");
            entity.Property(e => e.Locationdistrict)
                .HasMaxLength(50)
                .HasColumnName("locationdistrict");
            entity.Property(e => e.Locationward)
                .HasMaxLength(50)
                .HasColumnName("locationward");
            entity.Property(e => e.Packageid).HasColumnName("packageid");
            entity.Property(e => e.Totalsessions).HasColumnName("totalsessions");

            entity.Property(e => e.Priceperhour)
                .HasPrecision(12, 2)
                .HasColumnName("priceperhour");
            entity.Property(e => e.Totalamount)
                .HasPrecision(12, 2)
                .HasColumnName("totalamount");
            entity.Property(e => e.Currency)
                .HasMaxLength(10)
                .HasDefaultValueSql("'VND'::character varying")
                .HasColumnName("currency");
            entity.Property(e => e.Parentfee)
                .HasPrecision(12, 2)
                .HasColumnName("parentfee");
            entity.Property(e => e.Parentid)
                .HasMaxLength(50)
                .HasColumnName("parentid");
            entity.Property(e => e.Paymentcode)
                .HasMaxLength(50)
                .HasColumnName("paymentcode");
            entity.Property(e => e.Startdate)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("startdate");
            entity.Property(e => e.Paymentdueat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("paymentdueat");
            entity.Property(e => e.Paymentstatus)
                .HasMaxLength(20)
                .HasColumnName("paymentstatus");
            entity.Property(e => e.Platformfee)
                .HasPrecision(12, 2)
                .HasColumnName("platformfee");
            entity.Property(e => e.Promotionid).HasColumnName("promotionid");
            entity.Property(e => e.Remainingamount)
                .HasPrecision(12, 2)
                .HasColumnName("remainingamount");
            entity.Property(e => e.Remainingpaidat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("remainingpaidat");
            entity.Property(e => e.Sessionsremaining).HasColumnName("sessionsremaining");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasColumnName("status");
            entity.Property(e => e.Studentid)
                .HasMaxLength(50)
                .HasColumnName("studentid");
            entity.Property(e => e.Createdbyrole)
                .HasMaxLength(20)
                .HasColumnName("createdbyrole");
            entity.Property(e => e.Responsedeadline)
                .HasColumnName("responsedeadline");
            entity.Property(e => e.Payosbin).HasMaxLength(20).HasColumnName("payosbin");
            entity.Property(e => e.Payosaccountnumber).HasMaxLength(50).HasColumnName("payosaccountnumber");
            entity.Property(e => e.Payosaccountname).HasMaxLength(200).HasColumnName("payosaccountname");
            entity.Property(e => e.Payosdescription).HasMaxLength(100).HasColumnName("payosdescription");
            entity.Property(e => e.Payoscheckouturl).HasColumnName("payoscheckouturl");
            entity.Property(e => e.Payosqrcode).HasColumnName("payosqrcode");
            entity.Property(e => e.Tutorsubjectgradepriceid).HasColumnName("tutorsubjectgradepriceid");
            entity.Property(e => e.Tutorfee)
                .HasPrecision(12, 2)
                .HasColumnName("tutorfee");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutorid");
            entity.Property(e => e.Updatedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updatedat");
            entity.Property(e => e.Refundamount)
                .HasPrecision(12, 2)
                .HasColumnName("refundamount");
            entity.Property(e => e.Refundstatus)
                .HasMaxLength(50)
                .HasColumnName("refundstatus");

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

            entity.ToTable("chatchannels");

            entity.Property(e => e.Channelid).HasColumnName("channelid");
            entity.Property(e => e.Bookingid).HasColumnName("bookingid");
            entity.Property(e => e.Parentid)
                .HasMaxLength(50)
                .HasColumnName("parentid");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutorid");
            // Phase 4: Uncomment khi DB có cột studentid trên chatchannels
            entity.Property(e => e.Studentid)
                .HasMaxLength(50)
                .HasColumnName("studentid");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
            entity.Property(e => e.Lastmessageat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("lastmessageat");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'active'::character varying")
                .HasColumnName("status");

            entity.HasIndex(e => new { e.Parentid, e.Tutorid })
                .IsUnique()
                .HasFilter("parentid IS NOT NULL AND tutorid IS NOT NULL")
                .HasDatabaseName("ix_chatchannels_parentid_tutorid");

            // Phase 4: Uncomment khi DB có cột studentid trên chatchannels
            entity.HasIndex(e => new { e.Studentid, e.Tutorid })
                .IsUnique()
                .HasFilter("studentid IS NOT NULL AND tutorid IS NOT NULL")
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
                        j.ToTable("chatparticipants");
                        j.IndexerProperty<int>("Channelid").HasColumnName("channelid");
                        j.IndexerProperty<string>("Userid")
                            .HasMaxLength(50)
                            .HasColumnName("userid");
                    });
        });

        modelBuilder.Entity<Chatmessage>(entity =>
        {
            entity.HasKey(e => e.Messageid).HasName("chatmessages_pkey");

            entity.ToTable("chatmessages");

            entity.Property(e => e.Messageid).HasColumnName("messageid");
            entity.Property(e => e.Channelid).HasColumnName("channelid");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .HasColumnName("metadata");
            entity.Property(e => e.Messagetype)
                .HasMaxLength(20)
                .HasDefaultValueSql("'text'::character varying")
                .HasColumnName("messagetype");
            entity.Property(e => e.Senderid)
                .HasMaxLength(50)
                .HasColumnName("senderid");
            entity.Property(e => e.Isread)
                .HasDefaultValue(false)
                .HasColumnName("isread");
            entity.Property(e => e.Readat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("readat");

            entity.HasOne(d => d.Channel).WithMany(p => p.Chatmessages)
                .HasForeignKey(d => d.Channelid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("chatmessages_channelid_fkey");

            entity.HasOne(d => d.Sender).WithMany(p => p.Chatmessages)
                .HasForeignKey(d => d.Senderid)
                .HasConstraintName("chatmessages_senderid_fkey");
        });

        modelBuilder.Entity<Class>(entity =>
        {
            entity.HasKey(e => e.Classid).HasName("classes_pkey");

            entity.ToTable("classes");

            entity.HasIndex(e => e.Bookingid, "classes_bookingid_key").IsUnique();

            entity.HasIndex(e => e.Classcode, "classes_classcode_key").IsUnique();

            entity.HasIndex(e => e.Classcode, "idx_classes_code");

            entity.Property(e => e.Classid).HasColumnName("classid");
            entity.Property(e => e.Bookingid).HasColumnName("bookingid");
            entity.Property(e => e.Classcode)
                .HasMaxLength(20)
                .HasColumnName("classcode");
            entity.Property(e => e.Classname)
                .HasMaxLength(200)
                .HasColumnName("classname");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'active'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutorid");

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

            entity.Property(e => e.Disputeid).HasColumnName("disputeid");
            entity.Property(e => e.Bookingid).HasColumnName("bookingid");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
            entity.Property(e => e.Createdby)
                .HasMaxLength(50)
                .HasColumnName("createdby");
            entity.Property(e => e.Disputetype)
                .HasMaxLength(50)
                .HasColumnName("disputetype");
            entity.Property(e => e.Evidence)
                .HasColumnType("jsonb")
                .HasColumnName("evidence");
            entity.Property(e => e.Lessonid).HasColumnName("lessonid");
            entity.Property(e => e.Reason).HasColumnName("reason");
            entity.Property(e => e.Refundamount)
                .HasPrecision(12, 2)
                .HasColumnName("refundamount");
            entity.Property(e => e.Refundissued)
                .HasDefaultValue(false)
                .HasColumnName("refundissued");
            entity.Property(e => e.Refundpercentage).HasColumnName("refundpercentage");
            entity.Property(e => e.Resolutionnote).HasColumnName("resolutionnote");
            entity.Property(e => e.Resolvedat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("resolvedat");
            entity.Property(e => e.Resolvedby)
                .HasMaxLength(50)
                .HasColumnName("resolvedby");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasColumnName("status");

            entity.HasOne(d => d.Booking).WithMany(p => p.Disputes)
                .HasForeignKey(d => d.Bookingid)
                .HasConstraintName("disputes_bookingid_fkey");

            entity.HasOne(d => d.CreatedbyNavigation).WithMany(p => p.DisputeCreatedbyNavigations)
                .HasForeignKey(d => d.Createdby)
                .HasConstraintName("disputes_createdby_fkey");

            entity.HasOne(d => d.Lesson).WithMany(p => p.Disputes)
                .HasForeignKey(d => d.Lessonid)
                .HasConstraintName("disputes_lessonid_fkey");

            entity.HasOne(d => d.ResolvedbyNavigation).WithMany(p => p.DisputeResolvedbyNavigations)
                .HasForeignKey(d => d.Resolvedby)
                .HasConstraintName("disputes_resolvedby_fkey");
        });

        modelBuilder.Entity<Feedback>(entity =>
        {
            entity.HasKey(e => e.Feedbackid).HasName("feedbacks_pkey");

            entity.ToTable("feedbacks");

            entity.HasIndex(e => new { e.Bookingid, e.Fromuserid }, "feedbacks_bookingid_fromuserid_key").IsUnique();

            entity.Property(e => e.Feedbackid).HasColumnName("feedbackid");
            entity.Property(e => e.Bookingid).HasColumnName("bookingid");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
            entity.Property(e => e.Feedbacktype)
                .HasMaxLength(30)
                .HasDefaultValueSql("'post_lesson'::character varying")
                .HasColumnName("feedbacktype");
            entity.Property(e => e.Fromuserid)
                .HasMaxLength(50)
                .HasColumnName("fromuserid");
            entity.Property(e => e.Isvisible)
                .HasDefaultValue(true)
                .HasColumnName("isvisible");
            entity.Property(e => e.Lessonid).HasColumnName("lessonid");
            entity.Property(e => e.Rating).HasColumnName("rating");
            entity.Property(e => e.Repliedat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("repliedat");
            entity.Property(e => e.Replycomment).HasColumnName("replycomment");
            entity.Property(e => e.Touserid)
                .HasMaxLength(50)
                .HasColumnName("touserid");
            
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

            entity.HasOne(d => d.Lesson).WithMany(p => p.Feedbacks)
                .HasForeignKey(d => d.Lessonid)
                .HasConstraintName("feedbacks_lessonid_fkey");

            entity.HasOne(d => d.Touser).WithMany(p => p.FeedbackTousers)
                .HasForeignKey(d => d.Touserid)
                .HasConstraintName("feedbacks_touserid_fkey");
        });

        modelBuilder.Entity<Handoversummary>(entity =>
        {
            entity.HasKey(e => e.Summaryid).HasName("handoversummaries_pkey");

            entity.ToTable("handoversummaries");

            entity.Property(e => e.Summaryid).HasColumnName("summaryid");
            entity.Property(e => e.Attendancerate)
                .HasPrecision(5, 2)
                .HasColumnName("attendancerate");
            entity.Property(e => e.Averagescore)
                .HasPrecision(5, 2)
                .HasColumnName("averagescore");
            entity.Property(e => e.Frombookingid).HasColumnName("frombookingid");
            entity.Property(e => e.Fromtutorid)
                .HasMaxLength(50)
                .HasColumnName("fromtutorid");
            entity.Property(e => e.Generatedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("generatedat");
            entity.Property(e => e.Scoretrend)
                .HasMaxLength(20)
                .HasColumnName("scoretrend");
            entity.Property(e => e.Studentid)
                .HasMaxLength(50)
                .HasColumnName("studentid");
            entity.Property(e => e.Topicscovered).HasColumnName("topicscovered");
            entity.Property(e => e.Totalsessions).HasColumnName("totalsessions");
            entity.Property(e => e.Totutorid)
                .HasMaxLength(50)
                .HasColumnName("totutorid");
            entity.Property(e => e.Tutornotes).HasColumnName("tutornotes");

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

            entity.ToTable("learningmaterials");

            entity.HasIndex(e => e.Studentid, "idx_learningmaterials_student");

            entity.Property(e => e.Materialid).HasColumnName("materialid");
            entity.Property(e => e.Bookingid).HasColumnName("bookingid");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Filesize).HasColumnName("filesize");
            entity.Property(e => e.Filetype)
                .HasMaxLength(50)
                .HasColumnName("filetype");
            entity.Property(e => e.Fileurl).HasColumnName("fileurl");
            entity.Property(e => e.Ispublic)
                .HasDefaultValue(false)
                .HasColumnName("ispublic");
            entity.Property(e => e.Ownertype)
                .HasMaxLength(20)
                .HasColumnName("ownertype");
            entity.Property(e => e.Studentid)
                .HasMaxLength(50)
                .HasColumnName("studentid");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");
            entity.Property(e => e.Uploadedby)
                .HasMaxLength(50)
                .HasColumnName("uploadedby");

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

        modelBuilder.Entity<Lesson>(entity =>
        {
            entity.HasKey(e => e.Lessonid).HasName("lessons_pkey");

            entity.ToTable("lessons");

            entity.HasIndex(e => new { e.Istutorpresent, e.Isstudentpresent }, "idx_lessons_attendance");

            entity.HasIndex(e => e.Autoreportsent, "idx_lessons_autoreport").HasFilter("(autoreportsent = false)");

            entity.Property(e => e.Lessonid).HasColumnName("lessonid");
            entity.Property(e => e.Attendancenote).HasColumnName("attendancenote");
            entity.Property(e => e.Autoreportsent)
                .HasDefaultValue(false)
                .HasColumnName("autoreportsent");
            entity.Property(e => e.Autoreportsentat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("autoreportsentat");
            entity.Property(e => e.Bookingid).HasColumnName("bookingid");
            entity.Property(e => e.Checkintime)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("checkintime");
            entity.Property(e => e.Checkouttime)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("checkouttime");
            entity.Property(e => e.Confirmdeadline)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("confirmdeadline");
            entity.Property(e => e.Homework).HasColumnName("homework");
            entity.Property(e => e.Ismakeup)
                .HasDefaultValue(false)
                .HasColumnName("ismakeup");
            entity.Property(e => e.Issettled)
                .HasDefaultValue(false)
                .HasColumnName("issettled");
            entity.Property(e => e.Isstudentpresent).HasColumnName("isstudentpresent");
            entity.Property(e => e.Istutorpresent).HasColumnName("istutorpresent");
            entity.Property(e => e.Lessoncontent).HasColumnName("lessoncontent");
            entity.Property(e => e.Lessonprice)
                .HasPrecision(12, 2)
                .HasColumnName("lessonprice");
            entity.Property(e => e.Meetinglink)
                .HasMaxLength(1000)
                .HasColumnName("meetinglink");
            entity.Property(e => e.Noshowaction)
                .HasMaxLength(30)
                .HasColumnName("noshowaction");
            entity.Property(e => e.Originallessonid).HasColumnName("originallessonid");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
            entity.Property(e => e.Parentackat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("parentackat");
            entity.Property(e => e.Realend)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("realend");
            entity.Property(e => e.Realstart)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("realstart");
            entity.Property(e => e.Receiptsentat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("receiptsentat");
            entity.Property(e => e.Scheduledend)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("scheduledend");
            entity.Property(e => e.Scheduledstart)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("scheduledstart");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasDefaultValueSql("'scheduled'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.Studentid)
                .HasMaxLength(50)
                .HasColumnName("studentid");
            entity.Property(e => e.Submittedat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("submittedat");
            entity.Property(e => e.Isearlysubmission)
                .HasColumnName("isearlysubmission");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutorid");
            entity.Property(e => e.Tutornotes).HasColumnName("tutornotes");

            entity.HasOne(d => d.Booking).WithMany(p => p.Lessons)
                .HasForeignKey(d => d.Bookingid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("lessons_bookingid_fkey");

            entity.HasOne(d => d.Originallesson).WithMany(p => p.InverseOriginallesson)
                .HasForeignKey(d => d.Originallessonid)
                .HasConstraintName("lessons_originallessonid_fkey");

            entity.HasOne(d => d.Student).WithMany(p => p.Lessons)
                .HasForeignKey(d => d.Studentid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("lessons_studentid_fkey");

            entity.HasOne(d => d.Tutor).WithMany(p => p.Lessons)
                .HasForeignKey(d => d.Tutorid)
                .HasConstraintName("lessons_tutorid_fkey");
        });

        modelBuilder.Entity<Lessonreport>(entity =>
        {
            entity.HasKey(e => e.Reportid).HasName("lessonreports_pkey");

            entity.ToTable("lessonreports");

            entity.HasIndex(e => e.Lessonid, "lessonreports_lessonid_key").IsUnique();

            entity.Property(e => e.Reportid).HasColumnName("reportid");
            entity.Property(e => e.Attachments)
                .HasColumnType("jsonb")
                .HasColumnName("attachments");
            entity.Property(e => e.Contentcovered).HasColumnName("contentcovered");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
            entity.Property(e => e.Createdbytutorid)
                .HasMaxLength(50)
                .HasColumnName("createdbytutorid");
            entity.Property(e => e.Homeworkassigned).HasColumnName("homeworkassigned");
            entity.Property(e => e.Lessonid).HasColumnName("lessonid");
            entity.Property(e => e.Studentperformancerating).HasColumnName("studentperformancerating");

            entity.HasOne(d => d.Createdbytutor).WithMany(p => p.Lessonreports)
                .HasForeignKey(d => d.Createdbytutorid)
                .HasConstraintName("lessonreports_createdbytutorid_fkey");

            entity.HasOne(d => d.Lesson).WithOne(p => p.Lessonreport)
                .HasForeignKey<Lessonreport>(d => d.Lessonid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("lessonreports_lessonid_fkey");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Notificationid).HasName("notifications_pkey");

            entity.ToTable("notifications");

            entity.HasIndex(e => new { e.Userid, e.Isread }, "idx_notifications_user");

            entity.Property(e => e.Notificationid).HasColumnName("notificationid");
            entity.Property(e => e.Channel)
                .HasMaxLength(30)
                .HasDefaultValueSql("'app'::character varying")
                .HasColumnName("channel");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
            entity.Property(e => e.Deliverystatus)
                .HasMaxLength(20)
                .HasColumnName("deliverystatus");
            entity.Property(e => e.Isread)
                .HasDefaultValue(false)
                .HasColumnName("isread");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.Referenceid)
                .HasMaxLength(50)
                .HasColumnName("referenceid");
            entity.Property(e => e.Title)
                .HasMaxLength(200)
                .HasColumnName("title");
            entity.Property(e => e.Type)
                .HasMaxLength(50)
                .HasColumnName("type");
            entity.Property(e => e.Userid)
                .HasMaxLength(50)
                .HasColumnName("userid");
            entity.Property(e => e.Zaborequestid)
                .HasMaxLength(100)
                .HasColumnName("zaborequestid");

            entity.HasOne(d => d.User).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.Userid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("notifications_userid_fkey");
        });

        modelBuilder.Entity<Profilesuspension>(entity =>
        {
            entity.HasKey(e => e.Suspensionid).HasName("profilesuspensions_pkey");

            entity.ToTable("profilesuspensions");

            entity.Property(e => e.Suspensionid).HasColumnName("suspensionid");
            entity.Property(e => e.Createdby)
                .HasMaxLength(50)
                .HasColumnName("createdby");
            entity.Property(e => e.Enddate)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("enddate");
            entity.Property(e => e.Isactive)
                .HasDefaultValue(true)
                .HasColumnName("isactive");
            entity.Property(e => e.Reason).HasColumnName("reason");
            entity.Property(e => e.Startdate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("startdate");
            entity.Property(e => e.Suspensiontype)
                .HasMaxLength(30)
                .HasColumnName("suspensiontype");
            entity.Property(e => e.Userid)
                .HasMaxLength(50)
                .HasColumnName("userid");

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
                .HasColumnName("promotionid");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Discounttype)
                .HasMaxLength(20)
                .HasColumnName("discounttype");
            entity.Property(e => e.Discountvalue)
                .HasPrecision(12, 2)
                .HasColumnName("discountvalue");
            entity.Property(e => e.Enddate)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("enddate");
            entity.Property(e => e.Isactive)
                .HasDefaultValue(true)
                .HasColumnName("isactive");
            entity.Property(e => e.Maxdiscountamount)
                .HasPrecision(12, 2)
                .HasColumnName("maxdiscountamount");
            entity.Property(e => e.Minordervalue)
                .HasPrecision(12, 2)
                .HasColumnName("minordervalue");
            entity.Property(e => e.Startdate)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("startdate");
            entity.Property(e => e.Usagecount)
                .HasDefaultValue(0)
                .HasColumnName("usagecount");
            entity.Property(e => e.Usagelimit).HasColumnName("usagelimit");
        });



        modelBuilder.Entity<Studentgrade>(entity =>
        {
            entity.HasKey(e => e.Gradeid).HasName("studentgrades_pkey");

            entity.ToTable("studentgrades");

            entity.HasIndex(e => e.Bookingid, "idx_studentgrades_booking");

            entity.HasIndex(e => e.Studentid, "idx_studentgrades_student");

            entity.Property(e => e.Gradeid).HasColumnName("gradeid");
            entity.Property(e => e.Bookingid).HasColumnName("bookingid");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
            entity.Property(e => e.Examdate).HasColumnName("examdate");
            entity.Property(e => e.Examname)
                .HasMaxLength(200)
                .HasColumnName("examname");
            entity.Property(e => e.Examtype)
                .HasMaxLength(50)
                .HasColumnName("examtype");
            entity.Property(e => e.Maxscore)
                .HasPrecision(5, 2)
                .HasDefaultValueSql("10")
                .HasColumnName("maxscore");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.Score)
                .HasPrecision(5, 2)
                .HasColumnName("score");
            entity.Property(e => e.Studentid)
                .HasMaxLength(50)
                .HasColumnName("studentid");
            entity.Property(e => e.Subjectid).HasColumnName("subjectid");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutorid");

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

            entity.ToTable("studentprofiles");

            entity.HasIndex(e => e.Studentcode, "idx_studentprofiles_code");

            entity.HasIndex(e => e.Studentcode, "studentprofiles_studentcode_key").IsUnique();

            entity.Property(e => e.Studentid)
                .HasMaxLength(50)
                .HasColumnName("studentid");
            entity.Property(e => e.Avatarurl)
                .HasMaxLength(1000)
                .HasColumnName("avatarurl");
            entity.Property(e => e.Birthdate).HasColumnName("birthdate");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
            entity.Property(e => e.Fullname)
                .HasMaxLength(100)
                .HasColumnName("fullname");
            entity.Property(e => e.Gradelevelid).HasColumnName("gradelevelid");
            entity.Property(e => e.Learninggoals).HasColumnName("learninggoals");
            entity.Property(e => e.Linkeduserid)
                .HasMaxLength(50)
                .HasColumnName("linkeduserid");
            entity.Property(e => e.Parentid)
                .HasMaxLength(50)
                .HasColumnName("parentid");
            entity.Property(e => e.School)
                .HasMaxLength(255)
                .HasColumnName("school");
            entity.Property(e => e.Studentcode)
                .HasMaxLength(20)
                .HasColumnName("studentcode");
            entity.Property(e => e.Studentcodeexpiresat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("studentcodeexpiresat");
            entity.Property(e => e.Deletedat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("deletedat");

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

            entity.Property(e => e.Subjectid).HasColumnName("subjectid");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Subjectname)
                .HasMaxLength(100)
                .HasColumnName("subjectname");
        });

        modelBuilder.Entity<Systemconfig>(entity =>
        {
            entity.HasKey(e => e.Configid).HasName("systemconfigs_pkey");

            entity.ToTable("systemconfigs");

            entity.HasIndex(e => e.Configkey, "systemconfigs_configkey_key").IsUnique();

            entity.Property(e => e.Configid).HasColumnName("configid");
            entity.Property(e => e.Configkey)
                .HasMaxLength(100)
                .HasColumnName("configkey");
            entity.Property(e => e.Configvalue).HasColumnName("configvalue");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Updatedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updatedat");
            entity.Property(e => e.Updatedby)
                .HasMaxLength(50)
                .HasColumnName("updatedby");

            entity.HasOne(d => d.UpdatedbyNavigation).WithMany(p => p.Systemconfigs)
                .HasForeignKey(d => d.Updatedby)
                .HasConstraintName("systemconfigs_updatedby_fkey");
        });

        modelBuilder.Entity<Tutoravailability>(entity =>
        {
            entity.HasKey(e => e.Availabilityid).HasName("tutoravailability_pkey");

            entity.ToTable("tutoravailability");

            entity.Property(e => e.Availabilityid).HasColumnName("availabilityid");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
            entity.Property(e => e.Dayofweek).HasColumnName("dayofweek");
            entity.Property(e => e.Endtime).HasColumnName("endtime");
            entity.Property(e => e.Starttime).HasColumnName("starttime");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutorid");

            entity.HasOne(d => d.Tutor).WithMany(p => p.Tutoravailabilities)
                .HasForeignKey(d => d.Tutorid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("tutoravailability_tutorid_fkey");
        });

        modelBuilder.Entity<Tutorcertificate>(entity =>
        {
            entity.HasKey(e => e.Certificateid).HasName("tutorcertificates_pkey");

            entity.ToTable("tutorcertificates");

            entity.HasIndex(e => e.Tutorid, "idx_tutorcertificate_tutorid");

            entity.Property(e => e.Certificateid)
                .HasMaxLength(36)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("certificateid");
            entity.Property(e => e.Certificatefileurl)
                .HasMaxLength(2000)
                .HasColumnName("certificatefileurl");
            entity.Property(e => e.Certificatename)
                .HasMaxLength(200)
                .HasColumnName("certificatename");
            entity.Property(e => e.Certificatetype)
                .HasMaxLength(50)
                .HasColumnName("certificatetype");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
            entity.Property(e => e.Credentialid)
                .HasMaxLength(100)
                .HasColumnName("credentialid");
            entity.Property(e => e.Credentialurl)
                .HasMaxLength(2000)
                .HasColumnName("credentialurl");
            entity.Property(e => e.Issuingorganization)
                .HasMaxLength(200)
                .HasColumnName("issuingorganization");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(36)
                .HasColumnName("tutorid");
            entity.Property(e => e.Updatedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updatedat");
            entity.Property(e => e.Yearissued).HasColumnName("yearissued");

            // Thêm các thuộc tính mới ở đây
            entity.Property(e => e.Verificationstatus)
                .HasMaxLength(50)
                .HasDefaultValueSql("'pending_review'::character varying")
                .HasColumnName("verificationstatus");

            entity.Property(e => e.Verificationnote)
                .HasColumnName("verificationnote");

            entity.HasOne(d => d.Tutor).WithMany(p => p.Tutorcertificates)
                .HasForeignKey(d => d.Tutorid)
                .HasConstraintName("fk_tutorcertificate_tutor");
        });

        modelBuilder.Entity<Tutorprofile>(entity =>
        {
            entity.HasKey(e => e.Tutorid).HasName("tutorprofiles_pkey");

            entity.ToTable("tutorprofiles");

            entity.HasIndex(e => new { e.Isbankverified, e.Bankverifiedat }, "idx_tutorprofiles_bankverified")
                .HasFilter("isbankverified = true");

            entity.HasIndex(e => e.Bankverifycode, "idx_tutorprofiles_bankverifycode")
                .HasFilter("bankverifycode IS NOT NULL");

            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutorid");
            // map the CLR property to the exact DB column name (lowercase)
            entity.Property(p => p.Reviewedat).HasColumnName("reviewedat");
            entity.Property(p => p.Reviewedby).HasColumnName("reviewedby");
            // map other properties as needed
            entity.Property(e => e.Averagerating)
                .HasDefaultValueSql("0.0")
                .HasColumnName("averagerating");
            entity.Property(e => e.Bio).HasColumnName("bio");
            entity.Property(e => e.Completedhours)
                .HasDefaultValue(0)
                .HasColumnName("completedhours");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
            entity.Property(e => e.Education)
                .HasMaxLength(255)
                .HasColumnName("education");
            entity.Property(e => e.Experience).HasColumnName("experience");
            entity.Property(e => e.Gpa).HasColumnName("gpa");
            entity.Property(e => e.Gpascale).HasColumnName("gpascale");
            entity.Property(e => e.Headline)
                .HasMaxLength(200)
                .HasColumnName("headline");

            entity.Property(e => e.Ispublic)
                .HasDefaultValue(false)
                .HasColumnName("ispublic");
            entity.Property(e => e.Profilestatus)
                .HasMaxLength(30)
                .HasDefaultValueSql("'draft'::character varying")
                .HasColumnName("profilestatus");
            entity.Property(e => e.Rejectionnote).HasColumnName("rejectionnote");
            entity.Property(e => e.Subscriptiontype)
                .HasMaxLength(30)
                .HasDefaultValueSql("'free'::character varying")
                .HasColumnName("subscriptiontype");
            entity.Property(e => e.Teachingareacity)
                .HasMaxLength(50)
                .HasColumnName("teachingareacity");
            entity.Property(e => e.Teachingareadistrict)
                .HasMaxLength(50)
                .HasColumnName("teachingareadistrict");
            entity.Property(e => e.Totalreviews)
                .HasDefaultValue(0)
                .HasColumnName("totalreviews");
            entity.Property(e => e.Updatedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updatedat");
            entity.Property(e => e.Deletedat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("deletedat");
            entity.Property(e => e.Videointrourl)
                .HasMaxLength(1000)
                .HasColumnName("videointrourl");
            entity.Property(e => e.Bankname)
                .HasMaxLength(100)
                .HasColumnName("bankname");
            entity.Property(e => e.Bankaccountnumber)
                .HasMaxLength(50)
                .HasColumnName("bankaccountnumber");
            entity.Property(e => e.Bankaccountname)
                .HasMaxLength(100)
                .HasColumnName("bankaccountname");
            entity.Property(e => e.Isbankverified)
                .HasDefaultValue(false)
                .HasColumnName("isbankverified");
            entity.Property(e => e.Bankchangedat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("bankchangedat");
            entity.Property(e => e.Bankverifycode)
                .HasMaxLength(50)
                .HasColumnName("bankverifycode");
            entity.Property(e => e.Bankverifystatus)
                .HasMaxLength(30)
                .HasColumnName("bankverifystatus");
            entity.Property(e => e.Bankverifyrequested)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("bankverifyrequested");
            entity.Property(e => e.Bankverifiedat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("bankverifiedat");
            entity.Property(e => e.Bankverifyattempts)
                .HasDefaultValue(0)
                .HasColumnName("bankverifyattempts");

            entity.HasOne(d => d.Tutor).WithOne(p => p.Tutorprofile)
                .HasForeignKey<Tutorprofile>(d => d.Tutorid)
                .HasConstraintName("tutorprofiles_tutorid_fkey");
        });

        modelBuilder.Entity<Gradelevel>(entity =>
        {
            entity.HasKey(e => e.Gradelevelid).HasName("gradelevels_pkey");

            entity.ToTable("gradelevels");

            entity.HasIndex(e => e.Levelorder, "uq_gradelevels_levelorder").IsUnique();
            entity.HasIndex(e => e.Gradename, "uq_gradelevels_gradename").IsUnique();

            entity.Property(e => e.Gradelevelid).HasColumnName("gradelevelid");
            entity.Property(e => e.Gradename)
                .HasMaxLength(100)
                .HasColumnName("gradename");
            entity.Property(e => e.Levelorder).HasColumnName("levelorder");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
        });

        modelBuilder.Entity<Tutorsubjectgradeprice>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("tutorsubjectgradeprices_pkey");

            entity.ToTable("tutorsubjectgradeprices");

            entity.HasIndex(e => new { e.Tutorid, e.Subjectid, e.Gradelevelid }, "uq_tutorsubjectgradeprices_tutor_subject_grade").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Currency)
                .HasMaxLength(10)
                .HasDefaultValueSql("'VND'::character varying")
                .HasColumnName("currency");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
            entity.Property(e => e.Gradelevelid).HasColumnName("gradelevelid");
            entity.Property(e => e.Isactive)
                .HasDefaultValue(true)
                .HasColumnName("isactive");
            entity.Property(e => e.Priceperhour)
                .HasPrecision(12, 2)
                .HasColumnName("priceperhour");
            entity.Property(e => e.Subjectid).HasColumnName("subjectid");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutorid");
            entity.Property(e => e.Updatedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updatedat");
            entity.Property(e => e.Durationminutespersession)
                .HasDefaultValue(60)
                .HasColumnName("durationminutespersession");
            entity.Property(e => e.Sessionsperweek)
                .HasDefaultValue(1)
                .HasColumnName("sessionsperweek");

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

            entity.ToTable("tutorpackages");

            entity.Property(e => e.Packageid).HasColumnName("packageid");
            entity.Property(e => e.Packagetype).HasColumnName("packagetype");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
            entity.Property(e => e.Isactive)
                .HasDefaultValue(true)
                .HasColumnName("isactive");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutorid");
            entity.Property(e => e.Updatedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updatedat");

            entity.HasOne(d => d.Tutor).WithMany(p => p.Tutorpackages)
                .HasForeignKey(d => d.Tutorid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_tutorpackages_tutor");
        });

        modelBuilder.Entity<Tutorpackagefixedslot>(entity =>
        {
            entity.HasKey(e => e.Fixedslotid).HasName("tutorpackagefixedslots_pkey");

            entity.ToTable("tutorpackagefixedslots");

            entity.Property(e => e.Fixedslotid).HasColumnName("fixedslotid");
            entity.Property(e => e.Packageid).HasColumnName("packageid");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
            entity.Property(e => e.Dayofweek).HasColumnName("dayofweek");
            entity.Property(e => e.Endtime).HasColumnName("endtime");
            entity.Property(e => e.Starttime).HasColumnName("starttime");

            entity.HasOne(d => d.Package).WithMany(p => p.Tutorpackagefixedslots)
                .HasForeignKey(d => d.Packageid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_tutorpackagefixedslots_package");
        });

        modelBuilder.Entity<Tutorsubscription>(entity =>
        {
            entity.HasKey(e => e.Subscriptionid).HasName("tutorsubscriptions_pkey");

            entity.ToTable("tutorsubscriptions");

            entity.Property(e => e.Subscriptionid).HasColumnName("subscriptionid");
            entity.Property(e => e.Enddate)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("enddate");
            entity.Property(e => e.Isactive)
                .HasDefaultValue(true)
                .HasColumnName("isactive");
            entity.Property(e => e.Packagetype)
                .HasMaxLength(30)
                .HasColumnName("packagetype");
            entity.Property(e => e.Paymentstatus)
                .HasMaxLength(20)
                .HasDefaultValueSql("'pending'::character varying")
                .HasColumnName("paymentstatus");
            entity.Property(e => e.Price)
                .HasPrecision(12, 2)
                .HasColumnName("price");
            entity.Property(e => e.Startdate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("startdate");
            entity.Property(e => e.Tutorid)
                .HasMaxLength(50)
                .HasColumnName("tutorid");

            entity.HasOne(d => d.Tutor).WithMany(p => p.Tutorsubscriptions)
                .HasForeignKey(d => d.Tutorid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("tutorsubscriptions_tutorid_fkey");
        });

        modelBuilder.Entity<Topuprequest>(entity =>
        {
            entity.HasKey(e => e.Topuprequestid).HasName("topuprequests_pkey");

            entity.ToTable("topuprequests");

            entity.HasIndex(e => e.Ordercode, "topuprequests_ordercode_key").IsUnique();

            entity.HasIndex(e => new { e.Userid, e.Status }, "idx_topuprequests_userid_status");

            entity.Property(e => e.Topuprequestid).HasColumnName("topuprequestid");
            entity.Property(e => e.Ordercode).HasColumnName("ordercode");
            entity.Property(e => e.Userid)
                .HasMaxLength(50)
                .HasColumnName("userid");
            entity.Property(e => e.Amount)
                .HasPrecision(15, 2)
                .HasColumnName("amount");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'pending'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.Paymentlinkid)
                .HasMaxLength(255)
                .HasColumnName("paymentlinkid");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
            entity.Property(e => e.Completedat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("completedat");
            entity.Property(e => e.Expiresat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("expiresat");

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
                .HasColumnName("userid");
            entity.Property(e => e.Address)
                .HasMaxLength(255)
                .HasColumnName("address");
            entity.Property(e => e.Avatarurl)
                .HasMaxLength(1000)
                .HasColumnName("avatarurl");
            entity.Property(e => e.Birthdate).HasColumnName("birthdate");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
            entity.Property(e => e.Ekycrawdata).HasColumnName("ekycrawdata");
            entity.Property(e => e.Email)
                .HasMaxLength(100)
                .HasColumnName("email");
            entity.Property(e => e.Fullname)
                .HasMaxLength(100)
                .HasColumnName("fullname");
            entity.Property(e => e.Gender)
                .HasColumnName("gender")
                .HasConversion<short>();
            entity.Property(e => e.Googlecalendartoken).HasColumnName("googlecalendartoken");
            entity.Property(e => e.Fcmtoken)
                .HasMaxLength(500)
                .HasColumnName("fcmtoken");
            entity.Property(e => e.Idcardbackurl)
                .HasMaxLength(1000)
                .HasColumnName("idcardbackurl");
            entity.Property(e => e.Idcardfronturl)
                .HasMaxLength(1000)
                .HasColumnName("idcardfronturl");
            entity.Property(e => e.Identitynumber)
                .HasMaxLength(50)
                .HasColumnName("identitynumber");
            entity.Property(e => e.Isemailverified)
                .HasDefaultValue(false)
                .HasColumnName("isemailverified");
            entity.Property(e => e.Isidentityverified)
                .HasDefaultValue(false)
                .HasColumnName("isidentityverified");
            entity.Property(e => e.Isphoneverified)
                .HasDefaultValue(false)
                .HasColumnName("isphoneverified");
            entity.Property(e => e.Lastloginat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("lastloginat");
            entity.Property(e => e.Otpattempts)
                .HasDefaultValue(0)
                .HasColumnName("otpattempts");
            entity.Property(e => e.Otpcode)
                .HasMaxLength(10)
                .HasColumnName("otpcode");
            entity.Property(e => e.Otpexpiresat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("otpexpiresat");
            entity.Property(e => e.Password)
                .HasMaxLength(255)
                .HasColumnName("password");
            entity.Property(e => e.Phone)
                .HasMaxLength(20)
                .HasColumnName("phone");
            entity.Property(e => e.Primaryrole)
                .HasMaxLength(20)
                .HasColumnName("primaryrole");
            entity.Property(e => e.Status)
                .HasDefaultValue(1)
                .HasColumnName("status");
            entity.Property(e => e.Username)
                .HasMaxLength(50)
                .HasColumnName("username");
            entity.Property(e => e.Zabornotifyenabled)
                .HasDefaultValue(true)
                .HasColumnName("zabornotifyenabled");
            entity.Property(e => e.Zalouserid)
                .HasMaxLength(100)
                .HasColumnName("zalouserid");
            entity.Property(e => e.Parentcode)
                .HasMaxLength(10)
                .HasColumnName("parentcode");
            entity.Property(e => e.Parentcodeexpiresat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("parentcodeexpiresat");
            entity.Property(e => e.Hascompletedtour)
                .HasDefaultValue(false)
                .HasColumnName("hascompletedtour");
            entity.Property(e => e.Isdeactivated)
                .HasDefaultValue(false)
                .HasColumnName("isdeactivated");
            entity.Property(e => e.Deactivatedat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("deactivatedat");

            entity.HasIndex(e => e.Parentcode, "users_parentcode_key")
                .IsUnique()
                .HasFilter("parentcode IS NOT NULL");
        });



        modelBuilder.Entity<Userwarning>(entity =>
        {
            entity.HasKey(e => e.Warningid).HasName("userwarnings_pkey");

            entity.ToTable("userwarnings");

            entity.HasIndex(e => e.Userid, "idx_userwarnings_user");

            entity.Property(e => e.Warningid).HasColumnName("warningid");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
            entity.Property(e => e.Issuedby)
                .HasMaxLength(50)
                .HasColumnName("issuedby");
            entity.Property(e => e.Reason).HasColumnName("reason");
            entity.Property(e => e.Relatedbookingid).HasColumnName("relatedbookingid");
            entity.Property(e => e.Userid)
                .HasMaxLength(50)
                .HasColumnName("userid");
            entity.Property(e => e.Warninglevel).HasColumnName("warninglevel");

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

            entity.Property(e => e.Walletid).HasColumnName("walletid");
            entity.Property(e => e.Balance)
                .HasPrecision(15, 2)
                .HasDefaultValueSql("0")
                .HasColumnName("balance");
            entity.Property(e => e.Frozenbalance)
                .HasPrecision(15, 2)
                .HasDefaultValueSql("0")
                .HasColumnName("frozenbalance");
            entity.Property(e => e.Lastupdated)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("lastupdated");
            entity.Property(e => e.Userid)
                .HasMaxLength(50)
                .HasColumnName("userid");

            entity.HasOne(d => d.User).WithOne(p => p.Wallet)
                .HasForeignKey<Wallet>(d => d.Userid)
                .HasConstraintName("wallets_userid_fkey");
        });

        modelBuilder.Entity<Wallettransaction>(entity =>
        {
            entity.HasKey(e => e.Transactionid).HasName("wallettransactions_pkey");

            entity.ToTable("wallettransactions");

            entity.Property(e => e.Transactionid).HasColumnName("transactionid");
            entity.Property(e => e.Amount)
                .HasPrecision(15, 2)
                .HasColumnName("amount");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Ordercode).HasColumnName("ordercode");
            entity.Property(e => e.Referenceid).HasColumnName("referenceid");
            entity.Property(e => e.Referencetable)
                .HasMaxLength(50)
                .HasColumnName("referencetable");
            entity.Property(e => e.Transactiontype)
                .HasMaxLength(50)
                .HasColumnName("transactiontype");
            entity.Property(e => e.Walletid).HasColumnName("walletid");

            entity.HasOne(d => d.Wallet).WithMany(p => p.Wallettransactions)
                .HasForeignKey(d => d.Walletid)
                .HasConstraintName("wallettransactions_walletid_fkey");
        });

        modelBuilder.Entity<Withdrawalrequest>(entity =>
        {
            entity.HasKey(e => e.Withdrawalid).HasName("withdrawalrequests_pkey");

            entity.ToTable("withdrawalrequests");

            entity.Property(e => e.Withdrawalid).HasColumnName("withdrawalid");
            entity.Property(e => e.Accountholdername)
                .HasMaxLength(100)
                .HasColumnName("accountholdername");
            entity.Property(e => e.Accountnumber)
                .HasMaxLength(50)
                .HasColumnName("accountnumber");
            entity.Property(e => e.Amount)
                .HasPrecision(15, 2)
                .HasColumnName("amount");
            entity.Property(e => e.Bankname)
                .HasMaxLength(100)
                .HasColumnName("bankname");
            entity.Property(e => e.Processedat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("processedat");
            entity.Property(e => e.Requestedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("requestedat");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasColumnName("status");
            entity.Property(e => e.Userid)
                .HasMaxLength(50)
                .HasColumnName("userid");

            // PayOS tracking fields
            entity.Property(e => e.Payostransactionid)
                .HasMaxLength(255)
                .HasColumnName("payostransactionid");
            entity.Property(e => e.Payosstatus)
                .HasMaxLength(50)
                .HasColumnName("payosstatus");
            entity.Property(e => e.Payosresponsecode)
                .HasMaxLength(50)
                .HasColumnName("payosresponsecode");
            entity.Property(e => e.Payoserror)
                .HasColumnName("payoserror");
            entity.Property(e => e.Retrycount)
                .HasDefaultValue(0)
                .HasColumnName("retrycount");
            entity.Property(e => e.Lastretryat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("lastretryat");

            // Decision tracking fields
            entity.Property(e => e.Decision)
                .HasMaxLength(50)
                .HasColumnName("decision");
            entity.Property(e => e.Processedby)
                .HasMaxLength(50)
                .HasColumnName("processedby");

            entity.HasOne(d => d.User).WithMany(p => p.Withdrawalrequests)
                .HasForeignKey(d => d.Userid)
                .HasConstraintName("withdrawalrequests_userid_fkey");
        });

        modelBuilder.Entity<Systemalert>(entity =>
        {
            entity.HasKey(e => e.Alertid).HasName("systemalerts_pkey");

            entity.ToTable("systemalerts");

            entity.Property(e => e.Alertid).HasColumnName("alertid");
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
                .HasColumnName("resolvedat");
            entity.Property(e => e.Resolvedby)
                .HasMaxLength(50)
                .HasColumnName("resolvedby");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("refreshtokens_pkey");

            entity.ToTable("refreshtokens");

            entity.HasIndex(e => e.Tokenhash).IsUnique().HasDatabaseName("idx_refreshtokens_tokenhash");
            entity.HasIndex(e => e.Userid).HasDatabaseName("idx_refreshtokens_userid");
            entity.HasIndex(e => e.Tokenfamily).HasDatabaseName("idx_refreshtokens_tokenfamily");
            entity.HasIndex(e => e.Expiresat).HasDatabaseName("idx_refreshtokens_expiresat");

            entity.Property(e => e.Id).HasMaxLength(50).HasColumnName("id");
            entity.Property(e => e.Tokenhash).HasMaxLength(128).HasColumnName("tokenhash");
            entity.Property(e => e.Userid).HasMaxLength(50).HasColumnName("userid");
            entity.Property(e => e.Tokenfamily).HasMaxLength(50).HasColumnName("tokenfamily");
            entity.Property(e => e.Expiresat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("expiresat");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
            entity.Property(e => e.Revokedat)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("revokedat");
            entity.Property(e => e.Replacedbytokenhash).HasMaxLength(128).HasColumnName("replacedbytokenhash");

            entity.HasOne(d => d.User).WithMany(p => p.RefreshTokens)
                .HasForeignKey(d => d.Userid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_refreshtokens_user");
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
