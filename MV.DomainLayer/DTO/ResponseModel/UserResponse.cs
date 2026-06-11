namespace MV.DomainLayer.DTO.ResponseModel
{
    public class UserResponse
    {
        public string Userid { get; set; } = null!;
        public string Username { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Fullname { get; set; } = null!;
        public string? Phone { get; set; }

        public DateOnly? Birthdate { get; set; }
        public string? Address { get; set; }
        public string? Gender { get; set; }
        public string? Avatarurl { get; set; }
        public int? Status { get; set; }
        public DateTime? Createdat { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public string? Role { get; set; }
    }
}
