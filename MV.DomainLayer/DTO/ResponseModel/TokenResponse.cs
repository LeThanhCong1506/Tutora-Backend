namespace MV.DomainLayer.DTO.ResponseModel
{
    public class TokenResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public bool RequiresEmailVerification { get; set; }
        public bool RequiresPhoneVerification { get; set; }
        public bool RequiresPhoneInput { get; set; }
        public bool RequiresRoleSelection { get; set; }
        public string? SocialRegistrationToken { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
    }
}
