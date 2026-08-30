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
            var user = await _userRepository.GetUserByIdAsync(studentUserId)
                ?? throw new StudentNotFoundException();

            var result = await _identity.VerifyAndApplyAsync(user, request, AgeHelper.MinSelfBookingAge);

            await _userRepository.UpdateUserAsync(user);

            // Đồng bộ ngày sinh + họ tên sang Studentprofile để 2 bảng nhất quán.
            // Họ tên phải đồng bộ cả khi Studentprofile đã có sẵn giá trị: users.full_name vừa được
            // ghi đè theo CCCD, nếu ở đây chỉ điền-khi-trống thì trang Hồ sơ (đọc student_profiles)
            // và trang Tài khoản (đọc users) sẽ hiện hai cái tên khác nhau.
            var profile = await _studentRepository.FindByStudentOrLinkedUserAsync(studentUserId);
            if (profile == null)
            {
                // Có tài khoản học sinh nhưng thiếu hẳn dòng trong student_profiles (dữ liệu lệch từ
                // trước). Trước đây đoạn này lặng lẽ bỏ qua nên trang Hồ sơ trống trơn và bấm "Lưu hồ sơ"
                // còn văng lỗi không tìm thấy học sinh. Tạo bù theo đúng khuôn học sinh tự đăng ký.
                profile = new Studentprofile
                {
                    Studentid = studentUserId,
                    Linkeduserid = studentUserId,
                    Parentid = null,
                    Createdat = TimeZoneHelper.UtcNow
                };
                await _studentRepository.CreateAsync(profile);
                _logger.LogWarning(
                    "Student {UserId} thiếu Studentprofile, đã tạo bù trong lúc xác minh CCCD.", studentUserId);
            }

            if (result.DateOfBirth != null)
                profile.Birthdate = result.DateOfBirth;
            if (!string.IsNullOrWhiteSpace(user.Fullname))
                profile.Fullname = user.Fullname;

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

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Student {UserId} verified age, verified={Verified}", studentUserId, result.Verified);

            result.Response.Message = "Xác minh độ tuổi thành công. Bạn đã có thể tiến hành đặt lịch học.";
            return result.Response;
        }

        /// <summary>
        /// Trạng thái đủ điều kiện đặt lịch.
        /// </summary>
        public async Task<StudentBookingEligibilityResponse> GetBookingEligibilityAsync(string studentUserId)
        {
            var profile = await _studentRepository.FindByStudentOrLinkedUserAsync(studentUserId);
            var user = await _userRepository.GetUserByIdAsync(studentUserId);

            var resp = new StudentBookingEligibilityResponse();

            // Tài khoản do phụ huynh quản lý không tự đặt lịch được — chỉ phụ huynh mới có quyền
            // đặt lịch cho con (xem BookingService.CreateBookingAsync).
            if (profile?.Parentid != null)
            {
                resp.IsParentManaged = true;
                resp.CanBook = false;
                resp.ReasonCode = BookingErrorCodes.StudentManagedByParent;
                resp.Reason = "Tài khoản học sinh do phụ huynh quản lý không thể tự đặt lịch. Vui lòng nhờ phụ huynh đặt lịch giúp.";
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
            var profile = await _studentRepository.FindByStudentOrLinkedUserAsync(studentUserId)
                ?? throw new StudentNotFoundException();

            var trimmed = string.IsNullOrWhiteSpace(parentPhone) ? null : parentPhone.Trim();

            if (trimmed != null)
            {
                // SetParentPhoneRequest.[RegularExpression] đã đảm bảo trimmed khớp
                // ^(0|\+84)(\d{9,10})$ trước khi ModelState hợp lệ, nên tách phần đầu số ra là an toàn.
                var suffix = trimmed.StartsWith("+84") ? trimmed[3..] : trimmed[1..];
                var localForm = "0" + suffix;
                var e164Form = "+84" + suffix;

                // Users.Phone không được chuẩn hóa lúc lưu (có thể là "0..." hoặc "+84..." tùy nơi
                // nhập), nên phải thử cả hai dạng để không lọt số đã đăng ký ở dạng khác.
                var owner = await _userRepository.GetUserByPhoneAsync(localForm)
                    ?? await _userRepository.GetUserByPhoneAsync(e164Form);

                if (owner != null)
                {
                    if (owner.Userid == studentUserId)
                        throw new ParentPhoneMatchesOwnPhoneException();
                    throw new ParentPhoneAlreadyRegisteredException();
                }
            }

            profile.Parentphone = trimmed;
            await _dbContext.SaveChangesAsync();

            return profile.Parentphone;
        }
    }
}
