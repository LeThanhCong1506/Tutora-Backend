namespace MV.DomainLayer.DTO.ResponseModel
{
    /// <summary>
    /// Response sau khi Admin thay đổi role của user thành công.
    /// </summary>
    public class ChangeUserRoleResponse
    {
        /// <summary>Id của user được thay đổi role.</summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>Họ tên của user.</summary>
        public string? Fullname { get; set; }

        /// <summary>Role trước khi thay đổi.</summary>
        public string? PreviousRole { get; set; }

        /// <summary>Role mới sau khi thay đổi.</summary>
        public string NewRole { get; set; } = string.Empty;

        /// <summary>Id của Admin thực hiện thay đổi.</summary>
        public string ChangedByAdminId { get; set; } = string.Empty;

        /// <summary>Thời điểm thay đổi (giờ Việt Nam).</summary>
        public DateTime ChangedAt { get; set; }
    }
}
