using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    /// <summary>
    /// Xác minh CCCD/eKYC dùng chung cho tutor và học sinh (FPT.AI OCR + khớp tên + chống trùng + tùy chọn gate độ tuổi).
    /// </summary>
    public interface IEkycService
    {
        /// <summary>
        /// Chạy OCR ảnh CCCD mặt trước, kiểm tra và ghi danh tính vào <paramref name="user"/>.
        /// Ném <see cref="System.ArgumentException"/> khi lỗi file; ném <see cref="System.InvalidOperationException"/>
        /// khi vi phạm nghiệp vụ (ảnh mờ/giả, tên không khớp, chưa đủ tuổi, số CCCD trùng...).
        /// </summary>
        Task<EkycVerificationResult> VerifyAndApplyAsync(User user, UploadCccdRequest request, EkycVerificationOptions options);
    }
}
