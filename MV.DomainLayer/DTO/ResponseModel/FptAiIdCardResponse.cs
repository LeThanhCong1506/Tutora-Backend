namespace MV.DomainLayer.DTO.ResponseModel
{
    public class FptAiIdCardResponse
    {
        public int ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
        public List<FptAiResult> Data { get; set; }
    }
}
