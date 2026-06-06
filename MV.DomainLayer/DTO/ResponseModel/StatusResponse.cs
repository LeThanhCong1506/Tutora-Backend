namespace MV.DomainLayer.DTO.ResponseModel
{
    public class StatusResponse
    {
        public string? Status { get; set; }
        public string? Message { get; set; }
        public DateTime Timestamp { get; set; }

        public StatusResponse()
        {
            Timestamp = MV.DomainLayer.Helpers.VietnamTimeHelper.UtcNow;
        }
    }
}
