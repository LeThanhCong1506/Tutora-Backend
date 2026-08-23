using System.Xml.Serialization;

namespace MV.DomainLayer.DTO.ResponseModel
{
    [XmlRoot("Users")]
    public class UserExportListResponse
    {
        [XmlElement("User")]
        public List<UserExportResponse> Users { get; set; } = new List<UserExportResponse>();
    }
}
