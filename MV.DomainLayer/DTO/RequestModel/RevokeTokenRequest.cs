namespace MV.DomainLayer.DTO.RequestModel
{
    public class RevokeTokenRequest
    {
        public string RefreshToken { get; set; } = string.Empty;
    }
}
