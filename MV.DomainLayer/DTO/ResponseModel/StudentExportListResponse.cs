using System.Xml.Serialization;

namespace MV.DomainLayer.DTO.ResponseModel
{
    [XmlRoot("Students")]
    public class StudentExportListResponse
    {
        [XmlElement("Student")]
        public List<StudentExportResponse> Students { get; set; } = new List<StudentExportResponse>();
    }
}
