using System.Xml.Serialization;

namespace MV.DomainLayer.DTO.ResponseModel
{
    public class StudentExportResponse
    {
        [XmlAttribute("UserId")]
        public string Userid { get; set; } = null!;
        public string? Fullname { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public DateOnly? Birthdate { get; set; }
        public string? Identitynumber { get; set; }
        public string? Address { get; set; }
    }
}
