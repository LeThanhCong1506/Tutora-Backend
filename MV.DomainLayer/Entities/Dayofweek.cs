namespace MV.DomainLayer.Entities;

public partial class Dayofweek
{
    public int DayofweekId { get; set; }
    public string DayName { get; set; } = null!;
    public int DayOrder { get; set; }
    public DateTime? Createdat { get; set; }

    public virtual ICollection<Tutoravailability> Tutoravailabilities { get; set; } = new List<Tutoravailability>();
    public virtual ICollection<Tutorpackagefixedslot> Tutorpackagefixedslots { get; set; } = new List<Tutorpackagefixedslot>();
}