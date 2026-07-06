using System;

namespace MV.DomainLayer.Entities;

/// <summary>Bằng chứng khiếu nại (ảnh/file) upload riêng cho dispute. Khác với <see cref="Learningmaterial"/> (tài liệu học tập).</summary>
public partial class DisputeEvidence
{
    public int Disputeevidenceid { get; set; }

    public int Disputeid { get; set; }

    public string? Uploadedby { get; set; }

    public string Fileurl { get; set; } = null!;

    public string? Filetype { get; set; }

    public string? Description { get; set; }

    public DateTime? Createdat { get; set; }

    public virtual Dispute? Dispute { get; set; }

    public virtual User? UploadedbyNavigation { get; set; }
}
