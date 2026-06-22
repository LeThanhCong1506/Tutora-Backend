namespace MV.DomainLayer.DTO.ResponseModel
{
    public class TutorCccdUrlsResponse
    {
        public string TutorId { get; set; } = string.Empty;
        public string? TutorFullName { get; set; }
        public string? FrontImageUrl { get; set; }
        public string? BackImageUrl { get; set; }
        public bool IsIdentityVerified { get; set; }
        public int ExpiresInMinutes { get; set; }
    }
}
