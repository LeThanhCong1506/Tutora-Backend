using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;
using MV.DomainLayer.Utilities;

namespace MV.ApplicationLayer.Services
{
    /// <summary>
    /// Xác minh CCCD/eKYC dùng chung (FPT.AI OCR). Trích xuất từ luồng tutor để tutor và học sinh
    /// dùng chung một nguồn logic duy nhất. OCR đọc thất bại đủ ngưỡng lần liên tiếp (xem
    /// <see cref="HandleOcrFailureAsync"/>) → không chặn nữa nếu options.RequireOcr = false, chuyển
    /// sang chờ Admin xem thủ công — cùng cơ chế với <see cref="StudentIdentityService"/>.
    /// </summary>
    public class EkycService : IEkycService
    {
        private const double MinConfidence = 90.0;       // < 90% → ảnh mờ/giả
        private const string CccdBucket = StorageBucket.CccdFiles;
        private const int MaxOcrAttemptsBeforeManualReview = 2;

        private readonly IFptAiService _fptAiService;
        private readonly IEncryptionService _encryption;
        private readonly IUserRepository _userRepository;
        private readonly IFileStorageService _storageService;
        private readonly INotificationService _notificationService;
        private readonly IAppDbContext _context;
        private readonly ILogger<EkycService> _logger;

        public EkycService(
            IFptAiService fptAiService,
            IEncryptionService encryption,
            IUserRepository userRepository,
            IFileStorageService storageService,
            INotificationService notificationService,
            IAppDbContext context,
            ILogger<EkycService> logger)
        {
            _fptAiService = fptAiService ?? throw new ArgumentNullException(nameof(fptAiService));
            _encryption = encryption ?? throw new ArgumentNullException(nameof(encryption));
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<EkycVerificationResult> VerifyAndApplyAsync(User user, UploadCccdRequest request, EkycVerificationOptions options)
        {
            ValidateCccdImageFile(request.FrontImage, "mặt trước");
            ValidateCccdImageFile(request.BackImage, "mặt sau");

            // 1. Đọc bytes mặt trước → gửi trực tiếp cho FPT.AI OCR (không lưu ảnh, không qua URL).
            byte[] frontBytes;
            using (var ms = new MemoryStream())
            {
                await request.FrontImage.CopyToAsync(ms);
                frontBytes = ms.ToArray();
            }

            var ocrResult = await RunFptAiOcrAsync(frontBytes, request.FrontImage.FileName);

            // 2. Bắt buộc đọc được CCCD (nếu yêu cầu). RequireOcr=false (luồng tutor hiện tại) → OCR
            // thất bại đủ ngưỡng lần liên tiếp thì không chặn cứng nữa, chuyển cho Admin xem thủ công
            // (xem HandleOcrFailureAsync) thay vì cho qua ngay từ lần đầu như trước.
            if (ocrResult == null)
            {
                const string unreadableMessage = "Không đọc được thông tin trên CCCD. Vui lòng chụp lại rõ nét hơn.";
                if (options.RequireOcr)
                    throw new InvalidOperationException(unreadableMessage);

                return await HandleOcrFailureAsync(user, request, unreadableMessage);
            }

            DateOnly? dob = null;

            // 3. Độ tin cậy OCR.
            if (ocrResult.Probability < MinConfidence)
            {
                const string lowConfidenceMessage = "Ảnh CCCD không đủ rõ nét hoặc có dấu hiệu giả mạo. Vui lòng chụp lại.";
                if (options.RequireOcr)
                    throw new InvalidOperationException(lowConfidenceMessage);

                return await HandleOcrFailureAsync(user, request, lowConfidenceMessage);
            }

            // OCR đọc thành công (dù có thể vẫn bị từ chối ở bước tuổi/trùng CCCD ngay dưới đây) →
            // reset đếm thất bại, không để dồn từ những lần lỗi trước đó sang lần xác minh sau.
            user.Cccdocrfailedattempts = 0;

            // 4. Kiểm tra độ tuổi (nếu yêu cầu) — phải parse được DOB và đủ tuổi mới cho xác minh.
            if (!string.IsNullOrWhiteSpace(ocrResult.Dob) &&
                DateOnly.TryParseExact(ocrResult.Dob, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDob))
                dob = parsedDob;

            if (options.MinAgeRequired.HasValue)
            {
                if (dob == null)
                    throw new InvalidOperationException(
                        "Không đọc được ngày sinh trên CCCD. Vui lòng chụp lại rõ nét hơn.");

                var age = AgeHelper.CalculateAge(dob.Value);
                if (age < options.MinAgeRequired.Value)
                    throw new InvalidOperationException(
                        $"Bạn chưa đủ {options.MinAgeRequired.Value} tuổi nên chưa thể xác minh CCCD để đặt lịch học.");
            }

            // 5. Số CCCD không được trùng với tài khoản khác.
            if (!string.IsNullOrEmpty(ocrResult.Id) && ocrResult.Id != _encryption.Decrypt(user.Identitynumber))
            {
                var isUnique = await _userRepository.IsIdentityNumberUniqueAsync(_encryption.Encrypt(ocrResult.Id));
                if (!isUnique)
                    throw new InvalidOperationException(
                        "CCCD này đã được xác minh bởi tài khoản khác. Vui lòng liên hệ hỗ trợ nếu đây là nhầm lẫn.");
            }

            // 7. Lưu ảnh CCCD private (chỉ xem được qua signed URL, admin dùng để đối chiếu định danh).
            // Upload ảnh mới TRƯỚC rồi mới xoá ảnh cũ: nếu upload lỗi giữa chừng, DB vẫn trỏ tới ảnh cũ
            // còn nguyên trên đĩa thay vì trỏ tới file vừa bị xoá mất.
            var previousFrontUrl = user.Idcardfronturl;
            var previousBackUrl = user.Idcardbackurl;

            user.Idcardfronturl = await _storageService.UploadPrivateFileAsync(CccdBucket, user.Userid, request.FrontImage);
            user.Idcardbackurl = await _storageService.UploadPrivateFileAsync(CccdBucket, user.Userid, request.BackImage);

            // Ảnh cũ (nếu là lần re-upload) không còn ai trỏ tới — xoá cho khỏi rác đĩa.
            if (!string.IsNullOrEmpty(previousFrontUrl))
                await _storageService.DeleteFileAsync(CccdBucket, user.Userid, previousFrontUrl);
            if (!string.IsNullOrEmpty(previousBackUrl))
                await _storageService.DeleteFileAsync(CccdBucket, user.Userid, previousBackUrl);

            var verified = false;
            var profileDataUpdated = false;
            var pendingChanges = new List<EkycProfileFieldChange>();
            if (ocrResult != null)
            {
                user.Identitynumber = _encryption.Encrypt(ocrResult.Id);
                user.Ekycrawdata = _encryption.Encrypt(JsonSerializer.Serialize(new
                {
                    OcrResult = new { id = ocrResult.Id, name = ocrResult.Name, dob = ocrResult.Dob, sex = ocrResult.Sex, home = ocrResult.Home, address = ocrResult.Address },
                    VerifiedAt = TimeZoneHelper.UtcNow.ToString("o")
                }));

                verified = ocrResult.Probability >= MinConfidence;
                user.Isidentityverified = verified;

                // Việc so khớp/ghi các trường hồ sơ nằm ở EkycProfileSync để màn hình xác nhận,
                // luồng auto-fill và cờ "còn chờ xác nhận" (tiến trình hồ sơ) dùng CHUNG một logic.
                var ocrProfile = new EkycProfileSync.OcrProfileData
                {
                    Name = ocrResult.Name,
                    Dob = ocrResult.Dob,
                    Sex = ocrResult.Sex,
                    Address = ocrResult.Address,
                    Home = ocrResult.Home,
                };

                if (options.AutoFillProfile)
                {
                    profileDataUpdated = EkycProfileSync.Apply(user, ocrProfile).Count > 0;
                    user.Ekycprofileconfirmedat = TimeZoneHelper.UtcNow;
                }
                else
                {
                    // Chỉ xem trước — hồ sơ giữ nguyên cho tới khi chủ tài khoản bấm xác nhận.
                    // Quét mới làm mất hiệu lực lần xác nhận trước (dữ liệu đã khác), trừ khi
                    // CCCD trùng khớp hồ sơ hiện tại — khi đó không có gì để hỏi, đánh dấu xong luôn
                    // để FE không hiện nhắc xác nhận một danh sách rỗng.
                    pendingChanges = EkycProfileSync.Preview(user, ocrProfile);
                    user.Ekycprofileconfirmedat = pendingChanges.Count == 0 ? TimeZoneHelper.UtcNow : null;
                }
            }

            return new EkycVerificationResult
            {
                Ocr = ocrResult,
                DateOfBirth = dob,
                Verified = verified,
                Response = new CccdUploadResponse
                {
                    OcrSuccess = ocrResult != null,
                    ProfileDataUpdated = profileDataUpdated,
                    RequiresProfileConfirmation = pendingChanges.Count > 0,
                    PendingProfileChanges = pendingChanges,
                    IdentityNumber = MaskIdentityNumber(ocrResult?.Id),
                    FullName = ocrResult?.Name,
                    DateOfBirth = ocrResult?.Dob,
                    Gender = ocrResult?.Sex,
                    Hometown = ocrResult?.Home,
                    Address = ocrResult?.Address,
                    Message = ocrResult != null
                        ? "Upload và đọc CCCD thành công."
                        : "Upload thành công. Không đọc được thông tin CCCD, Admin sẽ xác minh thủ công."
                }
            };
        }

        public CccdProfileConfirmResponse ApplyStoredProfileData(User user)
        {
            // Nguồn dữ liệu là ekyc_raw_data đã lưu, KHÔNG phải giá trị client gửi lên —
            // client chỉ được quyền đồng ý/không đồng ý, không được quyền tự khai họ tên/ngày sinh.
            var data = EkycProfileSync.ParseStoredRawData(_encryption.Decrypt(user.Ekycrawdata))
                ?? throw new InvalidOperationException(
                    "Chưa có dữ liệu CCCD để cập nhật vào hồ sơ. Vui lòng quét CCCD trước.");

            var changes = EkycProfileSync.Apply(user, data);
            user.Ekycprofileconfirmedat = TimeZoneHelper.UtcNow;

            return new CccdProfileConfirmResponse
            {
                AppliedChanges = changes,
                FullName = user.Fullname,
                DateOfBirth = user.Birthdate?.ToString("dd/MM/yyyy"),
                Gender = EkycProfileSync.DescribeGender(user.Gender),
                Hometown = data.Home,
                Address = user.Address,
                Message = changes.Count > 0
                    ? "Đã cập nhật hồ sơ theo thông tin trên CCCD."
                    : "Hồ sơ của bạn đã khớp với CCCD, không có gì phải cập nhật.",
            };
        }

        // Private helpers

        /// <summary>
        /// OCR không đọc được CCCD (null/độ tin cậy thấp) khi options.RequireOcr = false. Dưới ngưỡng
        /// <see cref="MaxOcrAttemptsBeforeManualReview"/> lần liên tiếp: ném lỗi để người dùng tự chụp
        /// lại. ĐỦ ngưỡng: không chặn nữa — nhận và lưu luôn 2 ảnh, đánh dấu chờ Admin xem thủ công
        /// (KHÔNG đánh dấu Isidentityverified), báo cho Admin lẫn người dùng biết. Cùng cơ chế với
        /// <see cref="StudentIdentityService.VerifyAndApplyAsync"/>.
        /// </summary>
        private async Task<EkycVerificationResult> HandleOcrFailureAsync(User user, UploadCccdRequest request, string reasonMessage)
        {
            var attempts = await _userRepository.IncrementCccdOcrFailedAttemptsAsync(user.Userid);
            if (attempts < MaxOcrAttemptsBeforeManualReview)
                throw new InvalidOperationException(reasonMessage);

            _logger.LogWarning(
                "User {UserId} OCR CCCD thất bại {Attempts} lần liên tiếp — chuyển sang chờ Admin xem thủ công.",
                user.Userid, attempts);

            var previousFrontUrl = user.Idcardfronturl;
            var previousBackUrl = user.Idcardbackurl;

            user.Idcardfronturl = await _storageService.UploadPrivateFileAsync(CccdBucket, user.Userid, request.FrontImage);
            user.Idcardbackurl = await _storageService.UploadPrivateFileAsync(CccdBucket, user.Userid, request.BackImage);

            if (!string.IsNullOrEmpty(previousFrontUrl))
                await _storageService.DeleteFileAsync(CccdBucket, user.Userid, previousFrontUrl);
            if (!string.IsNullOrEmpty(previousBackUrl))
                await _storageService.DeleteFileAsync(CccdBucket, user.Userid, previousBackUrl);

            user.Isidentitypendingreview = true;
            // Đã escalate xong — reset đếm để lần xác minh KẾ TIẾP (nếu Admin từ chối) không bị cộng
            // dồn từ loạt thất bại này.
            user.Cccdocrfailedattempts = 0;

            await NotifyAdminsOfPendingReviewAsync(user);
            await NotifyUserOfPendingReviewAsync(user);

            return new EkycVerificationResult
            {
                Ocr = null,
                DateOfBirth = null,
                Verified = false,
                PendingManualReview = true,
                Response = new CccdUploadResponse
                {
                    OcrSuccess = false,
                    PendingManualReview = true,
                    Message = "Hệ thống chưa tự đọc được CCCD sau nhiều lần thử. Ảnh của bạn đã được " +
                        "gửi cho quản trị viên xem xét thủ công, chúng tôi sẽ thông báo kết quả sớm nhất."
                }
            };
        }

        /// <summary>
        /// Báo cho mọi Admin/Staff có quyền xem CCCD (TutorCccdView) biết có 1 hồ sơ đang chờ xem thủ
        /// công — không có "người phụ trách" riêng nên gửi cho tất cả, giống
        /// TutorService.NotifyAdminsOfProfileUpdateAsync. Chạy nền, không throw để không làm hỏng
        /// luồng upload chính nếu gửi thông báo lỗi.
        /// </summary>
        private async Task NotifyAdminsOfPendingReviewAsync(User user)
        {
            try
            {
                var reviewerIds = await PermissionRecipients.ResolveAsync(_context, Permissions.TutorCccdView);
                if (reviewerIds.Count == 0) return;

                await _notificationService.CreateNotificationsAsync(reviewerIds.Select(reviewerId => new NotificationRequest
                {
                    Userid = reviewerId,
                    Title = "CCCD cần xem thủ công",
                    Message = $"Người dùng {user.Fullname ?? user.Userid} xác minh CCCD tự động thất bại nhiều lần, cần Admin xem thủ công.",
                    Type = NotificationType.IdentityReviewRequested
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifyAdminsOfPendingReviewAsync failed for user {UserId}", user.Userid);
            }
        }

        /// <summary>Báo cho chính người dùng biết ảnh CCCD của họ đã được gửi cho Admin xem thủ công —
        /// tách khỏi <see cref="CccdUploadResponse.Message"/> (chỉ hiện ngay lúc bấm nút) vì đây là
        /// thông báo bền, người dùng vẫn thấy lại được dù rời trang trước khi đọc toast.</summary>
        private async Task NotifyUserOfPendingReviewAsync(User user)
        {
            try
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = user.Userid,
                    Title = "CCCD đang chờ xem xét thủ công",
                    Message = "Hệ thống chưa tự đọc được CCCD của bạn sau nhiều lần thử. Ảnh đã được gửi cho quản trị viên xem xét thủ công, chúng tôi sẽ thông báo kết quả sớm nhất.",
                    Type = NotificationType.IdentityPendingReview
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifyUserOfPendingReviewAsync failed for user {UserId}", user.Userid);
            }
        }

        private async Task<FptAiResult?> RunFptAiOcrAsync(byte[] imageBytes, string fileName)
        {
            try
            {
                using var stream = new MemoryStream(imageBytes);
                var response = await _fptAiService.VerifyIdCardAsync(stream, fileName);
                return response?.Data?.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FPT.AI OCR failed, proceeding without OCR data.");
                return null;
            }
        }

        private static void ValidateCccdImageFile(IFormFile file, string side)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException($"Ảnh CCCD {side} không được để trống.");
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
            if (!allowedExtensions.Contains(Path.GetExtension(file.FileName).ToLowerInvariant()))
                throw new ArgumentException($"Ảnh CCCD {side} chỉ chấp nhận định dạng JPG, JPEG hoặc PNG.");
            if (file.Length > 5 * 1024 * 1024)
                throw new ArgumentException($"Ảnh CCCD {side} phải nhỏ hơn 5MB.");
        }

        private static string? MaskIdentityNumber(string? number)
        {
            if (string.IsNullOrWhiteSpace(number) || number.Length < 6) return null;
            var visible = Math.Min(3, number.Length);
            var tail = Math.Min(4, number.Length - visible);
            var masked = new string('*', number.Length - visible - tail);
            return number[..visible] + masked + number[^tail..];
        }
    }
}
