using System.Xml.Serialization;

namespace MV.DomainLayer.DTO.ResponseModel
{
    [XmlType("Question")]
    public class QuestionExportResponse
    {
        [XmlAttribute("QuestionId")]
        public int Questionid { get; set; }

        public string Content { get; set; } = null!;

        [XmlArray("Options")]
        [XmlArrayItem("Option")]
        public List<OptionExportResponse> Options { get; set; } = new List<OptionExportResponse>();
    }
}
