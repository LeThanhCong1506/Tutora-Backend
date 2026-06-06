using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MV.DomainLayer.Entities;

[Table("fraud_logs")]
public class FraudLog
{
    [Key]
    [Column("logid")]
    public int Logid { get; set; }

    [Required]
    [MaxLength(50)]
    [Column("tutorid")]
    public string Tutorid { get; set; } = null!;

    [Column("withdrawalrequestid")]
    public int? Withdrawalrequestid { get; set; }

    [Required]
    [MaxLength(100)]
    [Column("rulename")]
    public string Rulename { get; set; } = null!;

    [Column("passed")]
    public bool Passed { get; set; }

    [Column("isflagged")]
    public bool Isflagged { get; set; }

    [Column("message")]
    public string? Message { get; set; }

    [Column("metadata", TypeName = "jsonb")]
    public string? Metadata { get; set; }

    [Column("checkedat")]
    public DateTime Checkedat { get; set; } = MV.DomainLayer.Helpers.VietnamTimeHelper.Now;

    public virtual Tutorprofile? Tutor { get; set; }
    public virtual Withdrawalrequest? Withdrawalrequest { get; set; }
}
