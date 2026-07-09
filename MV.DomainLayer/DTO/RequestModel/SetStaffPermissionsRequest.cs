using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    /// <summary>
    /// Request để Admin set lại toàn bộ danh sách quyền của một Staff.
    /// Danh sách gửi lên sẽ THAY THẾ hoàn toàn danh sách quyền hiện tại.
    /// </summary>
    public class SetStaffPermissionsRequest
    {
        [Required(ErrorMessage = "Danh sách quyền không được để trống (có thể là mảng rỗng để thu hồi hết quyền).")]
        public List<string> PermissionKeys { get; set; } = new();
    }
}
