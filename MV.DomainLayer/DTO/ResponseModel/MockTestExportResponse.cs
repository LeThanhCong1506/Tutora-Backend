using System.Xml.Serialization;

namespace MV.DomainLayer.DTO.ResponseModel
{
    [XmlRoot("MockTest")]
    public class MockTestExportResponse
    {
        [XmlAttribute("TestId")]
        public int Testid { get; set; }
        public string Testname { get; set; } = null!;
        public int? Durationminutes { get; set; }

        [XmlElement("SubjectId")]
        public int Subjectid { get; set; }

        [XmlArray("Questions")]
        [XmlArrayItem("Question")]
        public List<QuestionExportResponse> Questions { get; set; } = new List<QuestionExportResponse>();
    }
}
