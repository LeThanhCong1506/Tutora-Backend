using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Tutorcertificate
{
    public string Certificateid { get; set; } = null!;

    public string Tutorid { get; set; } = null!;

    public string Certificatename { get; set; } = null!;

    public string Certificatetype { get; set; } = null!;

    public string Issuingorganization { get; set; } = null!;

    public int? Yearissued { get; set; }

    public string? Credentialid { get; set; }

    public string? Credentialurl { get; set; }

    public string Certificatefileurl { get; set; } = null!;

    /// <summary>Ảnh trang 1 (JPG) tự render lúc upload — chỉ có khi Certificatefileurl là PDF. Null thì FE tự fallback icon.</summary>
    public string? Thumbnailurl { get; set; }

    /// <summary>
    /// Certificate verification status: "verified", "pending_review", "rejected"
    /// </summary>
    public string? Verificationstatus { get; set; }

    /// <summary>
    /// Admin note or auto-check result details
    /// </summary>
    public string? Verificationnote { get; set; }

    public DateTime? Createdat { get; set; }

    public DateTime? Updatedat { get; set; }

    public virtual Tutorprofile Tutor { get; set; } = null!;
}
