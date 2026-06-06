using System.Text.Json.Serialization;

namespace MV.DomainLayer.DTO.ResponseModel
{
    public class FptAiResult
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } // Số CCCD

        [JsonPropertyName("name")]
        public string Name { get; set; } // Họ tên

        [JsonPropertyName("dob")]
        public string Dob { get; set; } // Ngày sinh

        [JsonPropertyName("home")]
        public string Home { get; set; } // Quê quán

        [JsonPropertyName("address")]
        public string Address { get; set; } // Thường trú

        [JsonPropertyName("type_new")]
        public string Type { get; set; }

        [JsonPropertyName("sex")]
        public string Sex { get; set; }

        [JsonPropertyName("id_prob")]
        public string IdProb { get; set; }

        [JsonIgnore]
        public double Probability
        {
            get
            {
                if (string.IsNullOrEmpty(IdProb)) return 0;
                if (double.TryParse(IdProb, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var prob))
                {
                    return prob > 100 ? prob / 100.0 : prob;
                }
                return 0;
            }
        }
    }
}
