using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.Hubs;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Services
{
    /// <summary>
    /// Auth tập trung số điện thoại: đăng ký bằng phone + mật khẩu, bắt buộc verify OTP phone.
    /// Email là tùy chọn. Google/Zalo login đi qua service riêng (LoginService/ZaloAuthService).
    /// </summary>
    public class SimpleAuthService : ISimpleAuthService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IPasswordRepository _passwordRepository;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IOtpSender _otpSender;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SimpleAuthService> _logger;
        private readonly IDistributedCache _cache;
        private readonly IAiCreditService _aiCreditService;
        private readonly IHubContext<NotificationHub> _hubContext;

        private const int OtpExpiryMinutes = 5;
        private const int MaxOtpAttempts = 5;

        // Chống brute-force/spam đăng nhập: sai quá MaxLoginAttempts lần liên tiếp (tính theo
        // định danh email/SĐT/username) → khóa tạm LoginLockoutMinutes phút. Việc check khóa
        // nằm ở ngay đầu SimpleLoginAsync, TRƯỚC mọi truy vấn DB — nên khi đã bị khóa, các
        // request tiếp theo không còn chạm tới Postgres nữa (giảm tải DB, không chỉ chặn hack).
        private const int MaxLoginAttempts = 5;
        private const int LoginLockoutMinutes = 15;

        // Chống spam gửi OTP qua Zalo ZNS (mỗi lần gửi tốn phí thật): tối thiểu
        // OtpResendCooldownSeconds giữa 2 lần gửi liên tiếp cho CÙNG số điện thoại +
        // CÙNG mục đích (verify SĐT khi đăng ký, hoặc đặt lại mật khẩu) — khớp với
        // cooldown 60s FE đang hiển thị (VerifyPhonePage.tsx RESEND_COOLDOWN), nhưng
        // FE chỉ chặn trên UI; đây là chặn thật ở BE, áp dụng cho mọi client gọi trực
        // tiếp API (không riêng gì Tutora-FE).
        private const int OtpResendCooldownSeconds = 60;

        // Cooldown chỉ chặn TỐC ĐỘ gửi (60s/lần) — không chặn TỔNG SỐ trong ngày; ai đó
        // đổi IP liên tục vẫn có thể spam tới 1440 lần/ngày nếu chỉ có cooldown. Thêm giới
        // hạn tổng số lần gửi/24h cho CÙNG 1 số điện thoại để bịt khoảng hở này.
        private const int MaxOtpSendsPerDay = 5;

        public SimpleAuthService(
            IUnitOfWork unitOfWork,
            IPasswordRepository passwordRepository,
            IAuthenticationRepository authenticationRepository,
            IOtpSender otpSender,
            IConfiguration configuration,
            ILogger<SimpleAuthService> logger,
            IDistributedCache cache,
            IAiCreditService aiCreditService,
            IHubContext<NotificationHub> hubContext)
        {
            _unitOfWork = unitOfWork;
            _passwordRepository = passwordRepository;
            _authenticationRepository = authenticationRepository;
            _otpSender = otpSender;
            _configuration = configuration;
            _logger = logger;
            _cache = cache;
            _aiCreditService = aiCreditService;
            _hubContext = hubContext;
        }

        // "mobile" (từ header X-Client-Platform của tutora-mobile) là platform DUY NHẤT
        // được loại khỏi luật 1-phiên-web; mọi giá trị khác (kể cả thiếu header, tức web
        // thường/Zalo Mini App) đều coi là web và có thể bị đá / đá phiên web khác.
        private const string MobilePlatform = "mobile";

        private async Task KickOtherWebSessionsAsync(string userId, string newTokenId, string? platform, int expiryDays)
        {
            // App Flutter (mobile) không đọc/ghi gì ở đây — dừng ngay, không liên quan gì tới
            // cơ chế "1 phiên web" bên dưới, kể cả gián tiếp qua Redis.
            if (string.Equals(platform, MobilePlatform, StringComparison.OrdinalIgnoreCase))
                return;

            // Chỉ theo dõi ĐÚNG 1 con trỏ "phiên web hiện tại" của user trong Redis — không quét
            // hay kiểm tra platform của bất kỳ token nào khác. Nếu có phiên web cũ, đá nó; rồi
            // ghi đè con trỏ bằng token vừa tạo.
            var previousWebTokenId = await WebSessionTracker.GetCurrentAsync(_cache, userId);
            if (!string.IsNullOrEmpty(previousWebTokenId) && previousWebTokenId != newTokenId)
            {
                await _unitOfWork.RefreshTokenRepository.RevokeTokensAsync(new[] { previousWebTokenId });
                await _unitOfWork.SaveChangesAsync();
            }

            await WebSessionTracker.SetAsync(_cache, userId, newTokenId, TimeSpan.FromDays(expiryDays));

            try
            {
                await _hubContext.Clients.Group($"user:{userId}").SendAsync(
                    "ForceLogout",
                    new { reason = "new_login" });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not push ForceLogout for user {UserId}", userId);
            }
        }

        public async Task<TokenResponse> SimpleLoginAsync(SimpleLoginRequest request, string? platform = null)
        {
            try
            {
                if (string.IsNullOrEmpty(request.EmailOrPhone) || string.IsNullOrEmpty(request.Password))
                {
                    return new TokenResponse { ErrorMessage = "Bạn cần cung cấp email/số điện thoại và mật khẩu." };
                }

                // Check khóa TRƯỚC mọi truy vấn DB — request vào một định danh đang bị khóa
                // không chạm tới Postgres, tránh bị dùng để dội tải DB.
                var attemptKey = LoginAttemptKey(request.EmailOrPhone);
                var attemptEntry = await GetLoginAttemptAsync(attemptKey);
                if (attemptEntry != null && attemptEntry.LockedUntilUtc > MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow)
                {
                    var minutesLeft = Math.Max(1, (int)Math.Ceiling(
                        (attemptEntry.LockedUntilUtc - MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow).TotalMinutes));
                    return new TokenResponse
                    {
                        ErrorMessage = $"Tài khoản tạm khóa đăng nhập do nhập sai quá {MaxLoginAttempts} lần. Vui lòng thử lại sau {minutesLeft} phút."
                    };
                }

                User? user;

                if (request.EmailOrPhone.Contains("@"))
                {
                    var isValid = await _unitOfWork.UserRepository.CheckIfUserLoginCorrectAsync(
                        request.EmailOrPhone, request.Password);

                    if (!isValid)
                    {
                        await RecordFailedLoginAttemptAsync(attemptKey);
                        return new TokenResponse { ErrorMessage = "Email hoặc mật khẩu không đúng." };
                    }

                    user = await _unitOfWork.UserRepository.GetUserByEmailAsync(request.EmailOrPhone);
                }
                else if (request.EmailOrPhone.All(char.IsDigit) || request.EmailOrPhone.StartsWith("+"))
                {
                    var isValid = await _unitOfWork.UserRepository.CheckIfUserLoginCorrectByPhoneAsync(
                        request.EmailOrPhone, request.Password);

                    if (!isValid)
                    {
                        await RecordFailedLoginAttemptAsync(attemptKey);
                        return new TokenResponse { ErrorMessage = "Số điện thoại hoặc mật khẩu không đúng." };
                    }

                    user = await _unitOfWork.UserRepository.GetUserByPhoneAsync(request.EmailOrPhone);
                }
                else
                {
                    var isValid = await _unitOfWork.UserRepository.CheckIfUserLoginCorrectByUsernameAsync(
                        request.EmailOrPhone, request.Password);

                    if (!isValid)
                    {
                        await RecordFailedLoginAttemptAsync(attemptKey);
                        return new TokenResponse { ErrorMessage = "Tên người dùng hoặc mật khẩu không chính xác." };
                    }

                    user = await _unitOfWork.UserRepository.GetUserByUsernameAsync(request.EmailOrPhone);
                }

                // Mật khẩu đã đúng — xóa bộ đếm sai để không cộng dồn qua các lần đăng nhập sau.
                await ClearLoginAttemptAsync(attemptKey);

                if (user == null)
                {
                    return new TokenResponse { ErrorMessage = "Không tìm thấy người dùng." };
                }

                if (user.Status == 0)
                {
                    return new TokenResponse { ErrorMessage = "Tài khoản đã bị khóa." };
                }

                // Cổng xác thực SĐT là cơ chế onboarding chống-ảo cho KHÁCH HÀNG
                // (tự đăng ký). Tài khoản nội bộ (Staff/Admin) do Admin cấp — phone
                // là tùy chọn khi tạo — nên miễn cả 2 cổng; nếu không, staff không
                // phone sẽ vĩnh viễn bị chặn đăng nhập, còn có phone thì bị ép OTP
                // Zalo như khách hàng.
                // Tài khoản con do PHỤ HUYNH tạo (StudentService.CreateStudentAsync)
                // cũng không có Phone (chỉ có username + mật khẩu do parent xem 1 lần)
                // — miễn luôn 2 cổng này, nếu không con sẽ vĩnh viễn không đăng nhập
                // được dù đúng username/mật khẩu.
                var isInternalAccount = UserRole.IsInternal(user.Primaryrole);
                var skipsPhoneGate = isInternalAccount || await IsParentManagedChildAccountAsync(user.Userid);

                if (!skipsPhoneGate && string.IsNullOrWhiteSpace(user.Phone))
                {
                    return new TokenResponse
                    {
                        ErrorMessage = "Tài khoản chưa có số điện thoại. Vui lòng bổ sung và xác thực số điện thoại trước khi đăng nhập.",
                        RequiresPhoneInput = true
                    };
                }

                if (!skipsPhoneGate && user.Isphoneverified != true)
                {
                    return new TokenResponse
                    {
                        ErrorMessage = "Số điện thoại chưa được xác thực. Vui lòng xác thực OTP trước khi đăng nhập.",
                        RequiresPhoneVerification = true,
                        Phone = user.Phone
                    };
                }

                await _unitOfWork.UserRepository.UpdateLastLoginAtAsync(user.Userid, MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow);
                await _unitOfWork.SaveChangesAsync();

                return await CreateTokenResponseAsync(user, platform);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while logging in");
                return new TokenResponse { ErrorMessage = BuildErrorMessage(ex) };
            }
        }

        public async Task<TokenResponse> SimpleRegisterAsync(SimpleRegisterRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Phone))
                {
                    return new TokenResponse { ErrorMessage = "Số điện thoại là bắt buộc." };
                }

                if (string.IsNullOrEmpty(request.Password))
                {
                    return new TokenResponse { ErrorMessage = "Mật khẩu là bắt buộc." };
                }

                if (string.IsNullOrEmpty(request.FullName))
                {
                    return new TokenResponse { ErrorMessage = "Tên đầy đủ là bắt buộc." };
                }

                var rawRequestedRole = !string.IsNullOrEmpty(request.Role) ? request.Role : UserRole.Parent;
                if (!UserRole.SelfRegisterable.Contains(rawRequestedRole))
                {
                    return new TokenResponse { ErrorMessage = "Chức vụ này không cho phép tự đăng ký." };
                }

                // Chuẩn hóa về casing chuẩn trong UserRole.SelfRegisterable trước khi lưu Primaryrole.
                // Contains() ở trên so sánh không phân biệt hoa/thường nên "student"/"STUDENT" vẫn qua
                // được, nhưng IsInRole()/[Authorize(Roles=...)] so khớp claim role theo kiểu Ordinal
                // (phân biệt hoa/thường) — nếu lưu nguyên casing client gửi, tài khoản đó sẽ bị 403 ở
                // mọi endpoint yêu cầu đúng role dù Primaryrole "về ý nghĩa" là đúng.
                var requestedRole = NormalizeRole(rawRequestedRole) ?? rawRequestedRole;

                var phone = request.Phone.Trim();

                // Email tùy chọn — nếu có thì kiểm tra trùng.
                if (!string.IsNullOrWhiteSpace(request.Email))
                {
                    var existingByEmail = await _unitOfWork.UserRepository.GetUserByEmailAsync(request.Email);
                    if (existingByEmail != null)
                    {
                        return new TokenResponse { ErrorMessage = "Email đã tồn tại." };
                    }
                }

                var existingUserByPhone = await _unitOfWork.UserRepository.GetUserByPhoneAsync(phone);

                // SĐT đã tồn tại và đã xác thực → chặn.
                if (existingUserByPhone != null && existingUserByPhone.Isphoneverified == true)
                {
                    return new TokenResponse { ErrorMessage = "Số điện thoại đã được sử dụng." };
                }

                // SĐT đã có nhưng CHƯA xác thực (đăng ký dở) → cập nhật lại thông tin + gửi lại OTP.
                if (existingUserByPhone != null)
                {
                    existingUserByPhone.Password = _passwordRepository.HashPassword(request.Password);
                    existingUserByPhone.Fullname = request.FullName;
                    if (!string.IsNullOrWhiteSpace(request.Email))
                    {
                        existingUserByPhone.Email = request.Email;
                    }

                    // Lần đăng ký dở trước có thể đã tạo Studentprofile → cập nhật tên cho khớp bảng Users.
                    var existingProfile = await _unitOfWork.StudentRepository
                        .FindByStudentOrLinkedUserAsync(existingUserByPhone.Userid);
                    if (existingProfile != null)
                        existingProfile.Fullname = request.FullName;

                    await _unitOfWork.UserRepository.UpdateUserAsync(existingUserByPhone);
                    await _unitOfWork.SaveChangesAsync();

                    var blockReason = await GetOtpBlockReasonAsync(PhoneVerifyKey(phone));
                    if (blockReason != null)
                    {
                        return new TokenResponse
                        {
                            ErrorMessage = blockReason,
                            RequiresPhoneVerification = true,
                            Phone = phone
                        };
                    }

                    var resendCode = GenerateOtpCode();
                    await StoreOtpAsync(PhoneVerifyKey(phone), resendCode);
                    await _otpSender.SendOtpAsync(phone, resendCode);
                    await MarkOtpSentAsync(PhoneVerifyKey(phone));

                    return new TokenResponse
                    {
                        RequiresPhoneVerification = true,
                        Phone = phone,
                        ErrorMessage = string.Empty
                    };
                }

                var userId = Guid.NewGuid().ToString();
                var newUser = new User
                {
                    Userid = userId,
                    Email = request.Email,
                    Phone = phone,
                    Password = _passwordRepository.HashPassword(request.Password),
                    Fullname = request.FullName,
                    Status = 1,
                    Isemailverified = false,
                    Isphoneverified = false,
                    Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
                    Primaryrole = requestedRole
                };

                if (string.Equals(requestedRole, UserRole.Tutor, StringComparison.OrdinalIgnoreCase))
                {
                    newUser.Tutorprofile = new Tutorprofile
                    {
                        Tutorid = userId,
                        Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
                        Profilestatus = TutorProfileStatus.Draft
                    };
                }
                else if (string.Equals(requestedRole, UserRole.Student, StringComparison.OrdinalIgnoreCase))
                {
                    newUser.StudentprofileLinkedusers.Add(new Studentprofile
                    {
                        Studentid = userId,
                        Parentid = null,
                        Fullname = request.FullName,
                        // SĐT phụ huynh (tùy chọn) — chỉ để gửi ZNS theo dõi.
                        Parentphone = string.IsNullOrWhiteSpace(request.ParentPhone) ? null : request.ParentPhone.Trim(),
                        Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                    });
                }
                else if (string.Equals(requestedRole, UserRole.Parent, StringComparison.OrdinalIgnoreCase))
                {
                    newUser.Wallet = new Wallet
                    {
                        Userid = userId,
                        Balance = 0,
                        Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                    };
                }

                await _unitOfWork.UserRepository.CreateUserAsync(newUser);
                await _unitOfWork.SaveChangesAsync();

                try
                {
                    await _aiCreditService.GrantFreePackageAsync(userId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to grant Free AI credit to new user {UserId}", userId);
                }

                var otpCode = GenerateOtpCode();
                await StoreOtpAsync(PhoneVerifyKey(phone), otpCode);
                await _otpSender.SendOtpAsync(phone, otpCode);
                await MarkOtpSentAsync(PhoneVerifyKey(phone));

                return new TokenResponse
                {
                    RequiresPhoneVerification = true,
                    Phone = phone,
                    ErrorMessage = string.Empty
                };
            }
            catch (DbUpdateException ex) when (IsUniquePhoneConflict(ex))
            {
                _logger.LogWarning(ex, "Duplicate phone while registering");
                return new TokenResponse { ErrorMessage = "Số điện thoại đã được sử dụng." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while registering");
                return new TokenResponse { ErrorMessage = BuildErrorMessage(ex) };
            }
        }

        public async Task<TokenResponse> VerifyPhoneOtpAsync(VerifyPhoneOtpRequest request, string? platform = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Phone) || string.IsNullOrWhiteSpace(request.Otp))
                {
                    return new TokenResponse { ErrorMessage = "Số điện thoại và OTP là bắt buộc." };
                }

                var phone = request.Phone.Trim();
                var user = await _unitOfWork.UserRepository.GetUserByPhoneAsync(phone);
                if (user == null)
                {
                    return new TokenResponse { ErrorMessage = "Không tìm thấy người dùng." };
                }

                if (user.Isphoneverified == true)
                {
                    return await CreateTokenResponseAsync(user, platform);
                }

                var otpEntry = await GetOtpAsync(PhoneVerifyKey(phone));
                if (otpEntry == null)
                {
                    return new TokenResponse { ErrorMessage = "OTP đã hết hạn. Vui lòng gửi lại mã mới." };
                }

                if (otpEntry.Attempts >= MaxOtpAttempts)
                {
                    return new TokenResponse { ErrorMessage = "Quá nhiều lần nhập OTP không hợp lệ. Vui lòng gửi lại mã mới." };
                }

                if (!string.Equals(otpEntry.Code, request.Otp.Trim(), StringComparison.Ordinal))
                {
                    otpEntry.Attempts++;
                    await SaveOtpAsync(PhoneVerifyKey(phone), otpEntry);
                    return new TokenResponse { ErrorMessage = "Mã OTP không hợp lệ." };
                }

                user.Isphoneverified = true;
                await _unitOfWork.UserRepository.UpdateUserAsync(user);
                await _unitOfWork.SaveChangesAsync();
                await RemoveOtpAsync(PhoneVerifyKey(phone));

                return await CreateTokenResponseAsync(user, platform);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while verifying phone OTP");
                return new TokenResponse { ErrorMessage = BuildErrorMessage(ex) };
            }
        }

        public async Task<TokenResponse> ResendPhoneOtpAsync(ResendPhoneOtpRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Phone))
                {
                    return new TokenResponse { ErrorMessage = "Số điện thoại là bắt buộc." };
                }

                var phone = request.Phone.Trim();
                var user = await _unitOfWork.UserRepository.GetUserByPhoneAsync(phone);
                if (user == null)
                {
                    return new TokenResponse { ErrorMessage = "Không tìm thấy người dùng." };
                }

                if (user.Isphoneverified == true)
                {
                    return new TokenResponse { ErrorMessage = "Số điện thoại đã được xác thực." };
                }

                var blockReason = await GetOtpBlockReasonAsync(PhoneVerifyKey(phone));
                if (blockReason != null)
                {
                    return new TokenResponse
                    {
                        ErrorMessage = blockReason,
                        RequiresPhoneVerification = true,
                        Phone = phone
                    };
                }

                var otpCode = GenerateOtpCode();
                await StoreOtpAsync(PhoneVerifyKey(phone), otpCode);
                await _otpSender.SendOtpAsync(phone, otpCode);
                await MarkOtpSentAsync(PhoneVerifyKey(phone));

                return new TokenResponse
                {
                    RequiresPhoneVerification = true,
                    Phone = phone,
                    ErrorMessage = string.Empty
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while resending phone OTP");
                return new TokenResponse { ErrorMessage = BuildErrorMessage(ex) };
            }
        }

        public async Task<TokenResponse> ForgotPasswordAsync(ForgotPasswordRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Phone))
                {
                    return new TokenResponse { ErrorMessage = "Số điện thoại là bắt buộc." };
                }

                var phone = request.Phone.Trim();
                var user = await _unitOfWork.UserRepository.GetUserByPhoneAsync(phone);

                // Theo yêu cầu: báo lỗi rõ ràng khi SĐT chưa đăng ký thay vì luôn trả success.
                // Đánh đổi có chủ đích, chấp nhận mất lớp chống dò số điện thoại (enumeration)
                // để FE hiển thị đúng trạng thái cho người dùng.
                if (user == null)
                {
                    return new TokenResponse { ErrorMessage = "Số điện thoại chưa được đăng ký.", Phone = phone };
                }

                // Cooldown/giới hạn ngày vẫn check âm thầm (không phải yêu cầu thay đổi ở đây) —
                // nếu đang bị chặn gửi OTP thì coi như thành công, không gửi lại OTP mới.
                if (await GetOtpBlockReasonAsync(PhoneResetKey(phone)) == null)
                {
                    var otpCode = GenerateOtpCode();
                    await StoreOtpAsync(PhoneResetKey(phone), otpCode);
                    await _otpSender.SendOtpAsync(phone, otpCode);
                    await MarkOtpSentAsync(PhoneResetKey(phone));
                }

                return new TokenResponse { Phone = phone, ErrorMessage = string.Empty };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while processing forgot password (phone)");
                return new TokenResponse { ErrorMessage = BuildErrorMessage(ex) };
            }
        }

        public async Task<TokenResponse> ResetPasswordAsync(ResetPasswordRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Phone) || string.IsNullOrWhiteSpace(request.Otp)
                    || string.IsNullOrWhiteSpace(request.NewPassword))
                {
                    return new TokenResponse { ErrorMessage = "Số điện thoại, OTP và mật khẩu mới là bắt buộc." };
                }

                var phone = request.Phone.Trim();
                var user = await _unitOfWork.UserRepository.GetUserByPhoneAsync(phone);
                if (user == null)
                {
                    return new TokenResponse { ErrorMessage = "Yêu cầu không hợp lệ." };
                }

                var otpEntry = await GetOtpAsync(PhoneResetKey(phone));
                if (otpEntry == null)
                {
                    return new TokenResponse { ErrorMessage = "OTP đã hết hạn. Vui lòng yêu cầu lại." };
                }

                if (otpEntry.Attempts >= MaxOtpAttempts)
                {
                    return new TokenResponse { ErrorMessage = "Quá nhiều lần nhập OTP không hợp lệ. Vui lòng yêu cầu lại." };
                }

                if (!string.Equals(otpEntry.Code, request.Otp.Trim(), StringComparison.Ordinal))
                {
                    otpEntry.Attempts++;
                    await SaveOtpAsync(PhoneResetKey(phone), otpEntry);
                    return new TokenResponse { ErrorMessage = "Mã OTP không hợp lệ." };
                }

                if (_passwordRepository.VerifyPassword(request.NewPassword, user.Password))
                {
                    return new TokenResponse { ErrorMessage = "Mật khẩu mới không được trùng với mật khẩu cũ. Vui lòng chọn mật khẩu khác." };
                }

                user.Password = _passwordRepository.HashPassword(request.NewPassword);
                await _unitOfWork.UserRepository.UpdateUserAsync(user);
                await _unitOfWork.SaveChangesAsync();
                await RemoveOtpAsync(PhoneResetKey(phone));

                // Đặt lại mật khẩu qua OTP → cùng mức an ninh với đổi mật khẩu khi đã đăng
                // nhập: thu hồi MỌI phiên (web + mobile), không loại trừ platform nào.
                await _unitOfWork.RefreshTokenRepository.RevokeAllByUserIdAsync(user.Userid);
                await _unitOfWork.SaveChangesAsync();
                try
                {
                    await _hubContext.Clients.Group($"user:{user.Userid}").SendAsync(
                        "ForceLogout", new { reason = "password_changed" });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not push ForceLogout for user {UserId}", user.Userid);
                }

                return new TokenResponse { ErrorMessage = string.Empty };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while resetting password (phone)");
                return new TokenResponse { ErrorMessage = BuildErrorMessage(ex) };
            }
        }

        private async Task<TokenResponse> CreateTokenResponseAsync(User user, string? platform)
        {
            // Chốt chặn cuối trước khi phát token — miễn cho tài khoản nội bộ
            // (Staff/Admin, xác thực bằng email + mật khẩu, không qua OTP) và cho
            // tài khoản con do phụ huynh tạo (xem gate tương ứng ở SimpleLoginAsync).
            if (!UserRole.IsInternal(user.Primaryrole)
                && !await IsParentManagedChildAccountAsync(user.Userid)
                && (string.IsNullOrWhiteSpace(user.Phone) || user.Isphoneverified != true))
            {
                return new TokenResponse
                {
                    ErrorMessage = "Tài khoản phải có số điện thoại đã xác thực trước khi nhận token.",
                    RequiresPhoneInput = string.IsNullOrWhiteSpace(user.Phone),
                    RequiresPhoneVerification = !string.IsNullOrWhiteSpace(user.Phone),
                    Phone = user.Phone
                };
            }

            var role = await _unitOfWork.UserRepository.GetUserRoleByIdAsync(user.Userid);
            if (string.IsNullOrEmpty(role))
            {
                return new TokenResponse { ErrorMessage = "User role not found." };
            }

            var loginResponse = new LoginResponse
            {
                Userid = user.Userid,
                Username = user.Username ?? "",
                Fullname = user.Fullname,
                Email = user.Email ?? "",
                Phone = user.Phone ?? "",
                Role = role,
                Status = user.Status
            };

            var accessToken = _authenticationRepository.GenerateJwtToken(loginResponse);
            var rawRefreshToken = await CreateRefreshTokenAsync(user.Userid, platform);

            return new TokenResponse
            {
                AccessToken = accessToken,
                RefreshToken = rawRefreshToken,
                ErrorMessage = string.Empty
            };
        }

        /// <summary>
        /// True nếu userId là tài khoản con do phụ huynh tạo (StudentService.CreateStudentAsync
        /// — Studentprofile.Linkeduserid trỏ tới user này, Parentid khác null). Tự đăng ký
        /// (Studentprofile.Studentid == userId) luôn có Parentid null nên không tính.
        /// </summary>
        private async Task<bool> IsParentManagedChildAccountAsync(string userId)
        {
            var profile = await _unitOfWork.StudentRepository.FindByStudentOrLinkedUserAsync(userId);
            return profile?.Parentid != null;
        }

        private async Task<string> CreateRefreshTokenAsync(string userId, string? platform)
        {
            var rawToken = _authenticationRepository.GenerateRefreshToken();
            var tokenHash = _authenticationRepository.HashToken(rawToken);
            var expiryDays = int.TryParse(_configuration[ConfigurationKeys.Jwt.RefreshTokenExpiryDays], out var days) ? days : 7;

            var refreshToken = new RefreshToken
            {
                Id = Guid.NewGuid().ToString(),
                Tokenhash = tokenHash,
                Userid = userId,
                Tokenfamily = Guid.NewGuid().ToString(),
                Expiresat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddDays(expiryDays),
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            };

            await _unitOfWork.RefreshTokenRepository.CreateAsync(refreshToken);
            await _unitOfWork.SaveChangesAsync();

            // Đăng nhập mới (không phải refresh-rotation) → chỉ giữ 1 phiên web/Zalo Mini
            // App active; app Flutter (platform == "mobile") không đá ai và không bị đá.
            await KickOtherWebSessionsAsync(userId, refreshToken.Id, platform, expiryDays);

            return rawToken;
        }

        private static string GenerateOtpCode()
            => RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

        private static string? NormalizeRole(string? role)
        {
            if (string.IsNullOrWhiteSpace(role))
                return null;

            return UserRole.SelfRegisterable.FirstOrDefault(
                value => string.Equals(value, role.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        // Gộp cả chuỗi InnerException vào message trả về — message ngoài cùng của EF/Npgsql
        // (vd "likely due to a transient failure") chỉ là lời khuyên chung chung, lý do thật
        // luôn nằm ở InnerException.
        private static string BuildErrorMessage(Exception ex)
        {
            var parts = new List<string> { ex.Message };
            for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
                parts.Add(inner.Message);
            return "Error: " + string.Join(" | ", parts);
        }

        // ─── OTP storage (Redis via IDistributedCache), keyed by purpose+phone ───
        // Tách 2 namespace key để OTP verify-phone và OTP reset-password không đè nhau.
        private static string PhoneVerifyKey(string phone) => $"otp:phone:{phone.Trim()}";
        private static string PhoneResetKey(string phone) => $"otp:pwdreset:{phone.Trim()}";

        private sealed class OtpEntry
        {
            public string Code { get; set; } = "";
            public int Attempts { get; set; }
            public DateTime ExpiresAtUtc { get; set; }
        }

        private Task StoreOtpAsync(string key, string code)
        {
            var entry = new OtpEntry
            {
                Code = code,
                Attempts = 0,
                ExpiresAtUtc = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddMinutes(OtpExpiryMinutes)
            };
            return SaveOtpAsync(key, entry);
        }

        // Re-save với CÙNG absolute expiry để lần nhập sai không kéo dài tuổi thọ OTP.
        private Task SaveOtpAsync(string key, OtpEntry entry)
        {
            var json = JsonSerializer.Serialize(entry);
            var ttl = entry.ExpiresAtUtc - MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            if (ttl <= TimeSpan.Zero) ttl = TimeSpan.FromSeconds(1);
            return _cache.SetStringAsync(key, json, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl
            });
        }

        private async Task<OtpEntry?> GetOtpAsync(string key)
        {
            var json = await _cache.GetStringAsync(key);
            return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<OtpEntry>(json);
        }

        private Task RemoveOtpAsync(string key) => _cache.RemoveAsync(key);

        // ─── OTP resend cooldown (Redis), khóa theo cùng key purpose+phone với OTP entry ───
        private static string OtpCooldownKey(string otpKey) => $"{otpKey}:cooldown";

        private async Task<int> GetOtpCooldownSecondsLeftAsync(string otpKey)
        {
            var raw = await _cache.GetStringAsync(OtpCooldownKey(otpKey));
            if (string.IsNullOrEmpty(raw)
                || !DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var sentAtUtc))
                return 0;

            var secondsLeft = OtpResendCooldownSeconds
                - (MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow - sentAtUtc).TotalSeconds;
            return secondsLeft > 0 ? (int)Math.Ceiling(secondsLeft) : 0;
        }

        // ─── OTP daily send limit (Redis), cửa sổ cố định 24h tính từ lần gửi đầu tiên trong ngày ───
        private static string OtpDailyLimitKey(string otpKey) => $"{otpKey}:daily";

        private sealed class OtpDailyLimitEntry
        {
            public int Count { get; set; }
            public DateTime WindowStartUtc { get; set; }
        }

        private async Task<OtpDailyLimitEntry> GetOtpDailyLimitAsync(string otpKey)
        {
            var raw = await _cache.GetStringAsync(OtpDailyLimitKey(otpKey));
            if (!string.IsNullOrEmpty(raw))
            {
                var entry = JsonSerializer.Deserialize<OtpDailyLimitEntry>(raw)!;
                if (MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow - entry.WindowStartUtc < TimeSpan.FromHours(24))
                    return entry;
            }
            // Chưa có bản ghi, hoặc bản ghi cũ đã qua 24h → coi như ngày mới, đếm lại từ 0.
            return new OtpDailyLimitEntry { Count = 0, WindowStartUtc = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow };
        }

        private Task SaveOtpDailyLimitAsync(string otpKey, OtpDailyLimitEntry entry)
        {
            var remaining = TimeSpan.FromHours(24) - (MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow - entry.WindowStartUtc);
            if (remaining <= TimeSpan.Zero) remaining = TimeSpan.FromSeconds(1);
            return _cache.SetStringAsync(OtpDailyLimitKey(otpKey), JsonSerializer.Serialize(entry),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = remaining });
        }

        /// <summary>
        /// Check cả cooldown 60s VÀ giới hạn tổng số/ngày trước khi cho gửi OTP.
        /// Trả về thông báo lỗi nếu bị chặn, null nếu được phép gửi.
        /// </summary>
        private async Task<string?> GetOtpBlockReasonAsync(string otpKey)
        {
            var cooldownLeft = await GetOtpCooldownSecondsLeftAsync(otpKey);
            if (cooldownLeft > 0)
                return $"Vui lòng đợi {cooldownLeft} giây trước khi gửi lại mã.";

            var dailyEntry = await GetOtpDailyLimitAsync(otpKey);
            if (dailyEntry.Count >= MaxOtpSendsPerDay)
                return $"Số điện thoại này đã đạt giới hạn {MaxOtpSendsPerDay} lần gửi OTP trong 24 giờ. Vui lòng thử lại sau.";

            return null;
        }

        // Gọi ngay sau khi _otpSender.SendOtpAsync thành công — ghi cooldown 60s VÀ cộng dồn
        // bộ đếm ngày cho cùng 1 key purpose+phone.
        private async Task MarkOtpSentAsync(string otpKey)
        {
            await _cache.SetStringAsync(
                OtpCooldownKey(otpKey),
                MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.ToString("o"),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(OtpResendCooldownSeconds) });

            var dailyEntry = await GetOtpDailyLimitAsync(otpKey);
            dailyEntry.Count++;
            await SaveOtpDailyLimitAsync(otpKey, dailyEntry);
        }

        // ─── Login attempt lockout (Redis via IDistributedCache), keyed by định danh đăng nhập ───
        private static string LoginAttemptKey(string identifier) => $"login:fail:{identifier.Trim().ToLowerInvariant()}";

        private sealed class LoginAttemptEntry
        {
            public int FailCount { get; set; }
            public DateTime LockedUntilUtc { get; set; }
        }

        private async Task<LoginAttemptEntry?> GetLoginAttemptAsync(string key)
        {
            var json = await _cache.GetStringAsync(key);
            return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<LoginAttemptEntry>(json);
        }

        // Mỗi lần sai reset TTL của cả cửa sổ đếm — nghĩa là chuỗi lần sai phải liên tiếp trong
        // vòng LoginLockoutMinutes phút mới cộng dồn; im lặng quá lâu thì bộ đếm tự hết hạn về 0.
        private Task SaveLoginAttemptAsync(string key, LoginAttemptEntry entry) =>
            _cache.SetStringAsync(key, JsonSerializer.Serialize(entry), new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(LoginLockoutMinutes)
            });

        private async Task RecordFailedLoginAttemptAsync(string key)
        {
            var entry = await GetLoginAttemptAsync(key) ?? new LoginAttemptEntry();
            entry.FailCount++;
            if (entry.FailCount >= MaxLoginAttempts)
                entry.LockedUntilUtc = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddMinutes(LoginLockoutMinutes);
            await SaveLoginAttemptAsync(key, entry);
        }

        private Task ClearLoginAttemptAsync(string key) => _cache.RemoveAsync(key);

        private static bool IsUniquePhoneConflict(DbUpdateException ex)
        {
            var message = $"{ex.Message} {ex.InnerException?.Message}";
            return message.Contains("users_phone_key", StringComparison.OrdinalIgnoreCase);
        }
    }
}
