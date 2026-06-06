namespace MV.DomainLayer.DTO.RequestModel
{
    public class EmailRequest
    {
        public string? To { get; set; }
        public string? Subject { get; set; }
        public string? Body { get; set; }
    }

    public class VerifyOtpRequest
    {
        public string? Email { get; set; }
        public string? Otp { get; set; }
    }
}
