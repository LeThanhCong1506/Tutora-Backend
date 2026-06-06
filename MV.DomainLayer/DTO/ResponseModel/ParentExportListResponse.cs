using System.Xml.Serialization;

namespace MV.DomainLayer.DTO.ResponseModel
{
    [XmlRoot("Parents")]
    public class ParentExportListResponse
    {
        [XmlElement("Parent")]
        public List<ParentExportResponse> Parents { get; set; } = new List<ParentExportResponse>();
    }
}
