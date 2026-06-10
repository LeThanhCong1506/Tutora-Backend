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

    /// <summary>online | offline | both</summary>
    public string? Teachingmode { get; set; }

    public string? Bankname { get; set; }

    public string? Bankaccountnumber { get; set; }

    public string? Bankaccountname { get; set; }

    public bool? Isbankverified { get; set; }

    public DateTime? Bankchangedat { get; set; }

    public string? Bankverifycode { get; set; }

    public string? Bankverifystatus { get; set; }

    public DateTime? Bankverifyrequested { get; set; }

    public DateTime? Bankverifiedat { get; set; }

    public int Bankverifyattempts { get; set; }

    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    public virtual ICollection<Class> Classes { get; set; } = new List<Class>();

    public virtual ICollection<Handoversummary> HandoversummaryFromtutors { get; set; } = new List<Handoversummary>();

    public virtual ICollection<Handoversummary> HandoversummaryTotutors { get; set; } = new List<Handoversummary>();

    public virtual ICollection<Lessonreport> Lessonreports { get; set; } = new List<Lessonreport>();

    public virtual ICollection<Lesson> Lessons { get; set; } = new List<Lesson>();

    public virtual ICollection<Studentgrade> Studentgrades { get; set; } = new List<Studentgrade>();

    public virtual User Tutor { get; set; } = null!;

    public virtual ICollection<Tutoravailability> Tutoravailabilities { get; set; } = new List<Tutoravailability>();

    public virtual ICollection<Tutorcertificate> Tutorcertificates { get; set; } = new List<Tutorcertificate>();

    public virtual ICollection<Tutorpackage> Tutorpackages { get; set; } = new List<Tutorpackage>();

    public virtual ICollection<Tutorsubjectgradeprice> Tutorsubjectgradeprices { get; set; } = new List<Tutorsubjectgradeprice>();

    public virtual ICollection<Tutorsubscription> Tutorsubscriptions { get; set; } = new List<Tutorsubscription>();
}
