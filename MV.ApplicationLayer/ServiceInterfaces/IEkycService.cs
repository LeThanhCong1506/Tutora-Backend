using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    /// <summary>
    /// Xác minh CCCD/eKYC (FPT.AI OCR + chống trùng + tùy chọn cập nhật dữ liệu hồ sơ).
    /// </summary>
    public interface IEkycService
    {
        /// <summary>
        /// Chạy OCR ảnh CCCD mặt trước, kiểm tra và ghi danh tính vào <paramref name="user"/>.
        /// Ném <see cref="System.ArgumentException"/> khi lỗi file; ném <see cref="System.InvalidOperationException"/>
        /// khi vi phạm nghiệp vụ (ảnh mờ/giả, chưa đủ tuổi, số CCCD trùng...).
        /// </summary>
        Task<EkycVerificationResult> VerifyAndApplyAsync(User user, UploadCccdRequest request, EkycVerificationOptions options);

        /// <summary>
        /// Ghi dữ liệu CCCD ĐÃ LƯU (ekyc_raw_data) vào hồ sơ sau khi chủ tài khoản xác nhận,
        /// và đóng dấu thời điểm xác nhận. Không gọi OCR lại, không nhận dữ liệu từ client.
        /// Chỉ mutate <paramref name="user"/> — caller chịu trách nhiệm SaveChanges.
        /// Ném <see cref="System.InvalidOperationException"/> khi tài khoản chưa từng quét CCCD.
        /// </summary>
        CccdProfileConfirmResponse ApplyStoredProfileData(User user);
    }
}
