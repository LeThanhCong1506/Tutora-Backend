using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    public class UpdateTutorIntroductionRequest
    {
        [Required(ErrorMessage = "Vui lòng viết phần giới thiệu bản thân.")]
        [StringLength(2000, MinimumLength = 100, ErrorMessage = "Giới thiệu bản thân phải từ 100 đến 2000 ký tự.")]
        public string Bio { get; set; } = string.Empty;

        /// <summary>
        /// Học vị — FE gửi 1 giá trị trong danh sách cố định (Cao đẳng / Cử nhân / Kỹ sư /
        /// Thạc sĩ / Tiến sĩ / Phó Giáo sư / Giáo sư). Không ràng buộc enum ở đây để còn
        /// thêm học vị mới bên FE mà không phải deploy lại BE.
        /// </summary>
        [Required(ErrorMessage = "Vui lòng chọn học vị.")]
        [StringLength(100, ErrorMessage = "Học vị không được vượt quá 100 ký tự.")]
        public string Degree { get; set; } = string.Empty;

        /// <summary>
        /// Tên trường. Từ khi tách <see cref="Degree"/> ra, ô này CHỈ chứa tên trường nên
        /// không còn ràng buộc tối thiểu 10 ký tự như thời chuỗi gộp — "ĐH FPT" là hợp lệ.
        /// </summary>
        [Required(ErrorMessage = "Vui lòng nhập trường học.")]
        [StringLength(255, MinimumLength = 2, ErrorMessage = "Tên trường phải từ 2 đến 255 ký tự.")]
        public string Education { get; set; } = string.Empty;

        /// <summary>
        /// Thang điểm GPA (4.0 hoặc 10.0). Để trống được — GPA là thông tin tùy chọn,
        /// khớp với form bên FE (2 ô này không có dấu *).
        /// </summary>
        [RegularExpression(@"^(4(\.0)?|10(\.0)?)$", ErrorMessage = "Thang điểm chỉ nhận 4.0 hoặc 10.0.")]
        public double? GpaScale { get; set; }

        /// <summary>Điểm GPA. Để trống được; nếu có thì bắt buộc phải kèm <see cref="GpaScale"/>.</summary>
        [Range(0.0, 10.0, ErrorMessage = "Điểm GPA phải nằm trong khoảng 0.0 đến 10.0.")]
        public double? Gpa { get; set; }

        [Required(ErrorMessage = "Vui lòng mô tả kinh nghiệm giảng dạy.")]
        [StringLength(2000, MinimumLength = 50, ErrorMessage = "Kinh nghiệm giảng dạy phải từ 50 đến 2000 ký tự.")]
        public string Experience { get; set; } = string.Empty;
    }
}
