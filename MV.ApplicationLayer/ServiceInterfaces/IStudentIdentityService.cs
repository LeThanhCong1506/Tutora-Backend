using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    /// <summary>
    /// Xác minh CCCD/eKYC dành RIÊNG cho học sinh tự đăng ký (FPT.AI OCR + chống trùng + gate độ tuổi).
    /// Độc lập hoàn toàn với luồng tutor (<see cref="IEkycService"/>).
    /// </summary>
    public interface IStudentIdentityService
    {
        /// <summary>
        /// Chạy OCR ảnh CCCD mặt trước, kiểm tra và ghi danh tính vào <paramref name="user"/>.
        /// Bắt buộc đọc được CCCD và gate độ tuổi (mặc định >= 16): nếu chưa đủ tuổi → từ chối, KHÔNG đánh dấu xác minh.
        /// Ném <see cref="System.ArgumentException"/> khi lỗi file; ném <see cref="System.InvalidOperationException"/>
        /// khi vi phạm nghiệp vụ (không đọc được CCCD, ảnh mờ/giả, chưa đủ tuổi, số CCCD trùng...).
        /// </summary>
        Task<EkycVerificationResult> VerifyAndApplyAsync(User user, UploadCccdRequest request, int minAgeRequired);
    }
}
