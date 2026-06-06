namespace MV.DomainLayer.DTO.ResponseModel
{
    public class StudentCredentialsResponse
    {
        public string StudentId { get; set; } = null!;
        public string UserId { get; set; } = null!;
        public string Username { get; set; } = null!;
        public string TemporaryPassword { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string? ParentId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
