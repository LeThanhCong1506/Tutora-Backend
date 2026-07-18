using Microsoft.Extensions.Logging;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services
{
    /// <summary>
    /// Nghiệp vụ xác minh danh tính (CCCD) và điều kiện đặt lịch cho học sinh tự đăng ký.
    /// </summary>
    public partial class StudentService
    {
        /// <summary>
        /// Học sinh tự đăng ký xác minh CCCD để chứng minh đủ 16 tuổi.
        /// Dùng service eKYC RIÊNG cho học sinh (<see cref="IStudentIdentityService"/>)
        /// và gate độ tuổi: nếu DOB &lt; 16 → từ chối ngay, KHÔNG đánh dấu đã xác minh.
        /// </summary>
        public async Task<CccdUploadResponse> VerifyStudentCccdAsync(string studentUserId, UploadCccdRequest request)
        {
            var user = await _unitOfWork.UserRepository.GetUserByIdAsync(studentUserId)
                ?? throw new StudentNotFoundException();

            var result = await _identity.VerifyAndApplyAsync(user, request, AgeHelper.MinSelfBookingAge);

            await _unitOfWork.UserRepository.UpdateUserAsync(user);

            // Đồng bộ ngày sinh sang Studentprofile để 2 bảng nhất quán.
            var profile = await _unitOfWork.StudentRepository.FindByStudentOrLinkedUserAsync(studentUserId);
            if (profile != null)
            {
                if (result.DateOfBirth != null)
                    profile.Birthdate = result.DateOfBirth;
                if (string.IsNullOrWhiteSpace(profile.Fullname))
                    profile.Fullname = user.Fullname;
            }

            // Xác minh tuổi xong → tạo ví cho học sinh (KHÁC phụ huynh: phụ huynh có ví ngay khi tạo tài khoản,
            // học sinh chỉ có ví sau khi đủ điều kiện đặt lịch).
            if (result.Verified)
            {
                var existingWallet = await _walletRepository.GetByUserIdAsync(studentUserId);
                if (existingWallet == null)
                {
                    _walletRepository.Add(new Wallet
                    {
                        Userid = studentUserId,
                        Balance = 0,
                        Frozenbalance = 0,
                        Lastupdated = TimeZoneHelper.UtcNow
                    });
                    _logger.LogInformation("Created wallet for student {UserId} after age verification.", studentUserId);
                }
            }

            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Student {UserId} verified age, verified={Verified}", studentUserId, result.Verified);

            result.Response.Message = "Xác minh độ tuổi thành công. Bạn đã có thể tiến hành đặt lịch học.";
            return result.Response;
        }

        /// <summary>
        /// Trạng thái đủ điều kiện đặt lịch.
        /// </summary>
        public async Task<StudentBookingEligibilityResponse> GetBookingEligibilityAsync(string studentUserId)
        {
            var profile = await _unitOfWork.StudentRepository.FindByStudentOrLinkedUserAsync(studentUserId);
            var user = await _unitOfWork.UserRepository.GetUserByIdAsync(studentUserId);

            var resp = new StudentBookingEligibilityResponse();

            // Tài khoản do phụ huynh tạo/quản lý → phụ huynh mới được đặt lịch.
            if (profile?.Parentid != null)
            {
                resp.IsParentManaged = true;
                resp.CanBook = false;
                resp.ReasonCode = BookingErrorCodes.StudentManagedByParent;
                resp.Reason = "Bạn không thể đặt lịch học, vui lòng liên hệ bố mẹ hỗ trợ";
                return resp;
            }

            // Chưa hoàn thiện hồ sơ.
            var fullname = user?.Fullname ?? profile?.Fullname;
            if (string.IsNullOrWhiteSpace(fullname))
            {
                resp.NeedProfile = true;
                resp.CanBook = false;
                resp.ReasonCode = BookingErrorCodes.StudentIdentityNotVerified;
                resp.Reason = "Vui lòng hoàn thiện thông tin hồ sơ!";
                return resp;
            }

            // Chưa xác minh CCCD.
            if (user?.Isidentityverified != true)
            {
                resp.NeedAgeVerification = true;
                resp.CanBook = false;
                resp.ReasonCode = BookingErrorCodes.StudentIdentityNotVerified;
                resp.Reason = "Bạn cần xác minh độ tuổi để có thể đặt lịch học";
                return resp;
            }

            // Đã xác minh nhưng chưa đủ tuổi.
            if (user.Birthdate.HasValue)
                resp.Age = AgeHelper.CalculateAge(user.Birthdate.Value);

            if (!AgeHelper.IsOldEnoughToSelfBook(user.Birthdate))
            {
                resp.IsUnderage = true;
                resp.CanBook = false;
                resp.ReasonCode = BookingErrorCodes.StudentUnderage;
                resp.Reason = $"Bạn phải đủ {AgeHelper.MinSelfBookingAge} tuổi mới có thể đặt lịch học";
                return resp;
            }

            resp.CanBook = true;
            return resp;
        }

        /// <summary>
        /// Học sinh tự đăng ký nhập/cập nhật SĐT phụ huynh (để nhận ZNS theo dõi).
        /// Lưu trên Studentprofile của học sinh.
        /// </summary>
        public async Task<string?> SetParentPhoneAsync(string studentUserId, string? parentPhone)
        {
            var profile = await _unitOfWork.StudentRepository.FindByStudentOrLinkedUserAsync(studentUserId)
                ?? throw new StudentNotFoundException();

            profile.Parentphone = string.IsNullOrWhiteSpace(parentPhone) ? null : parentPhone.Trim();
            await _unitOfWork.SaveChangesAsync();

            return profile.Parentphone;
        }
    }
}
