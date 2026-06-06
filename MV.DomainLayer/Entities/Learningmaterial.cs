using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Learningmaterial
{
    public int Materialid { get; set; }

    public string? Studentid { get; set; }

    public int? Bookingid { get; set; }

    public string? Uploadedby { get; set; }

    public string Ownertype { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public string? Filetype { get; set; }

    public string Fileurl { get; set; } = null!;

    public int? Filesize { get; set; }

    public bool? Ispublic { get; set; }

    public DateTime? Createdat { get; set; }

    public virtual Booking? Booking { get; set; }

    public virtual Studentprofile? Student { get; set; }

    public virtual User? UploadedbyNavigation { get; set; }
}
