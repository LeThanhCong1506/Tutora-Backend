using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace MV.DomainLayer.Entities;

public partial class Booking
{
    public int Bookingid { get; set; }

    public string? Parentid { get; set; }

    public string? Studentid { get; set; }

    public string? Tutorid { get; set; }

    public int? Tutorsubjectgradepriceid { get; set; }

    public int? Packageid { get; set; }

    [NotMapped]
    public int? Subjectid
    {
        get => Tutorsubjectgradeprice?.Subjectid;
        set { }
    }

    public int? Promotionid { get; set; }

    public decimal? Discountapplied { get; set; }

    public int? Totalsessions { get; set; }

    [NotMapped]
    public string? Packagetype { get; set; }

    [NotMapped]
    public int? Sessioncount
    {
        get => Totalsessions;
        set => Totalsessions = value;
    }

    public int? Sessionsremaining { get; set; }

    public decimal? Priceperhour { get; set; }

    public decimal? Totalamount { get; set; }

    public string? Currency { get; set; }

    [NotMapped]
    public decimal? Price
    {
        get => Totalamount;
        set => Totalamount = value;
    }

    public decimal? Finalprice { get; set; }

    public string? Paymentcode { get; set; }

    public DateTime? Startdate { get; set; }

    [NotMapped]
    public string? Schedule { get; set; }

    public string? Locationcity { get; set; }

    public string? Locationdistrict { get; set; }

    public string? Locationward { get; set; }

    public string? Locationdetail { get; set; }

    public string? Status { get; set; }

    public string? Paymentstatus { get; set; }

    public DateTime? Paymentdueat { get; set; }

    public DateTime? Createdat { get; set; }

    public DateTime? Updatedat { get; set; }

    public decimal? Depositamount { get; set; }

    public DateTime? Depositpaidat { get; set; }

    public decimal? Remainingamount { get; set; }

    public DateTime? Remainingpaidat { get; set; }

    public string? Escrowstatus { get; set; }

    public decimal? Platformfee { get; set; }

    public decimal? Parentfee { get; set; }

    public decimal? Tutorfee { get; set; }

    public decimal? Refundamount { get; set; }

    public string? Refundstatus { get; set; }

    public string? Cancellationreason { get; set; }

    public string? Cancelledby { get; set; }

    public DateTime? Cancelledat { get; set; }

    public DateTime? Graceperiodends { get; set; }

    // Phase 4: Uncomment khi đã chạy SQL: ALTER TABLE bookings ADD COLUMN createdbyrole varchar(20);
    public string? Createdbyrole { get; set; }

    // Phase 13: Tutor response deadline (auto-cancel if tutor doesn't respond within 24h)
    public DateTime? Responsedeadline { get; set; }

    // PayOS display fields — cache để reuse link mà không tạo link mới (giữ mã CK ổn định)
    public string? Payosbin { get; set; }
    public string? Payosaccountnumber { get; set; }
    public string? Payosaccountname { get; set; }
    public string? Payosdescription { get; set; }
    public string? Payoscheckouturl { get; set; }
    public string? Payosqrcode { get; set; }

    public virtual ICollection<Chatchannel> Chatchannels { get; set; } = new List<Chatchannel>();

    public virtual Class? Class { get; set; }

    public virtual ICollection<Dispute> Disputes { get; set; } = new List<Dispute>();

    public virtual ICollection<Feedback> Feedbacks { get; set; } = new List<Feedback>();

    public virtual ICollection<Handoversummary> Handoversummaries { get; set; } = new List<Handoversummary>();

    public virtual ICollection<Learningmaterial> Learningmaterials { get; set; } = new List<Learningmaterial>();

    public virtual ICollection<Lesson> Lessons { get; set; } = new List<Lesson>();

    public virtual User? Parent { get; set; }

    public virtual Promotion? Promotion { get; set; }

    public virtual Studentprofile? Student { get; set; }

    public virtual ICollection<Studentgrade> Studentgrades { get; set; } = new List<Studentgrade>();

    public virtual Tutorpackage? Package { get; set; }

    [NotMapped]
    public virtual Subject? Subject => Tutorsubjectgradeprice?.Subject;

    public virtual Tutorprofile? Tutor { get; set; }

    public virtual Tutorsubjectgradeprice? Tutorsubjectgradeprice { get; set; }

    public virtual ICollection<Userwarning> Userwarnings { get; set; } = new List<Userwarning>();
}
