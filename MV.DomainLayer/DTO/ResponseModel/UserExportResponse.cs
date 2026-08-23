using System.Xml.Serialization;

namespace MV.DomainLayer.DTO.ResponseModel
{
    public class UserExportResponse
    {
        [XmlAttribute("UserId")]
        public string Userid { get; set; } = null!;
        public string? Fullname { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Role { get; set; }
        public int? Status { get; set; }
        public DateOnly? Birthdate { get; set; }
        public string? Identitynumber { get; set; }
        public string? Address { get; set; }
        public DateTime? Createdat { get; set; }
    }
}
