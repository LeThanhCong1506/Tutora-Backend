using System.Xml.Serialization;

namespace MV.DomainLayer.DTO.ResponseModel
{
    [XmlType("Option")]
    public class OptionExportResponse
    {
        [XmlAttribute("IsCorrect")]
        public bool Iscorrect { get; set; }

        [XmlText]
        public string Optiontext { get; set; } = null!;
    }
}
