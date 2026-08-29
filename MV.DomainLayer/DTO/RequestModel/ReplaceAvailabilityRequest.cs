using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    /// <summary>
    /// Toàn bộ lịch rảnh mong muốn của gia sư, thay thế trọn vẹn lịch đang lưu.
    ///
    /// Vì sao cần endpoint này thay vì để client tự ghép DELETE → PATCH → POST: các ràng buộc chéo
    /// (khung cố định của gói phải nằm trong lịch rảnh) chỉ đúng khi soi TRẠNG THÁI CUỐI. Chuỗi
    /// nhiều request đi qua những trạng thái trung gian hợp lệ về mặt kỹ thuật nhưng vi phạm ràng
    /// buộc — vd bước DELETE xoá hết thứ Hai trước khi bước POST thêm lại khung mới. Gộp thành một
    /// request cho phép kiểm tra đúng một lần, trên đúng cái mà người dùng thật sự muốn lưu.
    ///
    /// Cho phép danh sách RỖNG: gia sư tạm ngưng nhận booking là thao tác hợp lệ (miễn là không
    /// bỏ rơi khung của gói cố định đang bật).
    /// </summary>
    public class ReplaceAvailabilityRequest
    {
        [Required(ErrorMessage = "Availabilities list is required")]
        public List<CreateAvailabilityRequest> Availabilities { get; set; } = new();
    }
}
