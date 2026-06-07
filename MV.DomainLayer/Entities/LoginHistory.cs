using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MV.DomainLayer.Entities;

[Table("login_history")]
public class LoginHistory
{
    [Key]
    [Column("logid")]
    public int Logid { get; set; }

    [Required]
    [MaxLength(50)]
    [Column("userid")]
    public string Userid { get; set; } = null!;

    [Required]
    [MaxLength(45)]
    [Column("ipaddress")]
    public string Ipaddress { get; set; } = null!;

    [Column("useragent")]
    public string? Useragent { get; set; }

    [Column("loggedat")]
    public DateTime Loggedat { get; set; } = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

    public virtual User? User { get; set; }
}
