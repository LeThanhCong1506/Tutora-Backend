using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace MV.DomainLayer.Entities;

public partial class Tutorprofile
{
    public string Tutorid { get; set; } = null!;

    public string? Bio { get; set; }

    public string? Headline { get; set; }

    public string? Videointrourl { get; set; }

    /// <summary>
    /// Học vị (Cử nhân, Thạc sĩ, Tiến sĩ...) — chọn từ danh sách cố định ở FE.
    /// NULL với hồ sơ tạo trước 23/08/2026, khi học vị còn nằm lẫn trong <see cref="Education"/>.
    /// </summary>
    public string? Degree { get; set; }

    /// <summary>Tên trường. Từ 23/08/2026 KHÔNG còn chứa học vị — xem <see cref="Degree"/>.</summary>
    public string? Education { get; set; }

    public double? Gpascale { get; set; }

    public double? Gpa { get; set; }

    public string? Experience { get; set; }

    [NotMapped]
    public decimal? Hourlyrate
    {
        get => Tutorsubjectgradeprices?.Where(p => p.Isactive).OrderBy(p => p.Priceperhour).Select(p => (decimal?)p.Priceperhour).FirstOrDefault();
        set { }
    }

    public string? Teachingareacity { get; set; }

    public string? Teachingareadistrict { get; set; }

    /// <summary>
    /// Main status: Draft | PendingApproval | Rejected | Active
    /// </summary>
    public string? Profilestatus { get; set; }

    public bool? Ispublic { get; set; }

    /// <summary>Tutor tự bật/tắt nhận booking mới. false = ẩn khỏi marketplace + chặn booking mới.</summary>
    public bool Isacceptingbookings { get; set; } = true;

    public double? Averagerating { get; set; }

    public int? Totalreviews { get; set; }

    public int? Completedhours { get; set; }

    public DateTime? Createdat { get; set; }

    public DateTime? Updatedat { get; set; }

    public DateTime? Deletedat { get; set; }

    // ❌ XÓA: Verificationstatus, Verifiedat, Verifiedby
    
    /// <summary>
    /// Lý do Admin reject (nếu có)
    /// </summary>
    public string? Rejectionnote { get; set; }

    /// <summary>
    /// Admin đã review (approve/reject)
    /// </summary>
    public string? Reviewedby { get; set; }
    
    public DateTime? Reviewedat { get; set; }

    public string? Subscriptiontype { get; set; }

    // /// <summary>online | offline | both</summary>
    // public string? Teachingmode { get; set; }


    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    public virtual ICollection<Class> Classes { get; set; } = new List<Class>();

    public virtual ICollection<Handoversummary> HandoversummaryFromtutors { get; set; } = new List<Handoversummary>();

    public virtual ICollection<Handoversummary> HandoversummaryTotutors { get; set; } = new List<Handoversummary>();

    public virtual ICollection<ClassSessionReport> ClassSessionReports { get; set; } = new List<ClassSessionReport>();

    public virtual ICollection<ClassSession> ClassSessions { get; set; } = new List<ClassSession>();

    public virtual ICollection<Studentgrade> Studentgrades { get; set; } = new List<Studentgrade>();

    public virtual User Tutor { get; set; } = null!;

    public virtual ICollection<Tutoravailability> Tutoravailabilities { get; set; } = new List<Tutoravailability>();

    public virtual ICollection<Tutorcertificate> Tutorcertificates { get; set; } = new List<Tutorcertificate>();

    public virtual ICollection<Tutorpackage> Tutorpackages { get; set; } = new List<Tutorpackage>();

    public virtual ICollection<Tutorsubjectgradeprice> Tutorsubjectgradeprices { get; set; } = new List<Tutorsubjectgradeprice>();

    public virtual ICollection<Tutorsubscription> Tutorsubscriptions { get; set; } = new List<Tutorsubscription>();
}
