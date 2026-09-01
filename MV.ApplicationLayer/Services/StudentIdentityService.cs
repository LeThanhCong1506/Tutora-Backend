using System.Globalization;
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

namespace MV.ApplicationLayer.Services
{
    /// <summary>
    /// Dùng ngày sinh để chứng minh đủ 16 tuổi; ảnh CCCD lưu private (chỉ Admin xem được qua signed URL).
    /// Có lưu số CCCD (mã hóa AES) DUY NHẤT để chống 1 CCCD dùng cho nhiều tài khoản né gate tuổi.
    /// OCR (FPT.AI) đọc thất bại đủ ngưỡng lần liên tiếp → không chặn nữa, chuyển sang chờ Admin xem thủ công
    /// (xem <see cref="HandleOcrFailureAsync"/>).
    /// </summary>
    public class StudentIdentityService : IStudentIdentityService
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
        private readonly ILogger<StudentIdentityService> _logger;

        public StudentIdentityService(
            IFptAiService fptAiService,
            IEncryptionService encryption,
            IUserRepository userRepository,
            IFileStorageService storageService,
            INotificationService notificationService,
            IAppDbContext context,
            ILogger<StudentIdentityService> logger)
        {
            _fptAiService = fptAiService ?? throw new ArgumentNullException(nameof(fptAiService));
            _encryption = encryption ?? throw new ArgumentNullException(nameof(encryption));
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<EkycVerificationResult> VerifyAndApplyAsync(User user, UploadCccdRequest request, int minAgeRequired)
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

            // 2. Học sinh BẮT BUỘC đọc được CCCD (cần DOB để chứng minh tuổi). OCR không đọc được có
            // thể do ảnh mờ/thiếu sáng dù CCCD thật — thất bại đủ ngưỡng lần liên tiếp thì không chặn cứng
            // nữa, chuyển cho Admin xem thủ công (xem HandleOcrFailureAsync).
            if (ocrResult == null)
                return await HandleOcrFailureAsync(
                    user, request, "Không đọc được thông tin trên CCCD. Vui lòng chụp lại rõ nét hơn.");

            // 3. Độ tin cậy OCR.
            if (ocrResult.Probability < MinConfidence)
                return await HandleOcrFailureAsync(
                    user, request, "Ảnh CCCD không đủ rõ nét hoặc có dấu hiệu giả mạo. Vui lòng chụp lại.");

            // 4. CHỈ lấy NGÀY SINH để xác minh độ tuổi. KHÔNG check tên, KHÔNG lưu ảnh/raw eKYC.
            DateOnly? dob = null;
            if (!string.IsNullOrWhiteSpace(ocrResult.Dob) &&
                DateOnly.TryParseExact(ocrResult.Dob, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDob))
                dob = parsedDob;

            if (dob == null)
                return await HandleOcrFailureAsync(
                    user, request, "Không đọc được ngày sinh trên CCCD. Vui lòng chụp lại rõ nét hơn.");

            // OCR đọc thành công → reset đếm thất bại, không để dồn từ những lần lỗi trước đó sang
            // lần xác minh sau. Chỉ set trên entity (đang được caller — StudentService.
            // VerifyStudentCccdAsync — track và SaveChanges ở cuối luồng); nếu bị từ chối ngay dưới
            // đây (chưa đủ tuổi/CCCD trùng) và ném exception thì reset này không được lưu ngay, nhưng
            // vô hại: sẽ tự ghi đè lại đúng ở lần OCR thành công kế tiếp.
            user.Cccdocrfailedattempts = 0;

            var age = AgeHelper.CalculateAge(dob.Value);
            if (age < minAgeRequired)
                throw new InvalidOperationException(
                    $"Bạn chưa đủ {minAgeRequired} tuổi nên chưa thể đặt lịch học.");

            // 5. Chống trùng: 1 số CCCD không được xác minh cho nhiều tài khoản khác nhau
            //    Dùng AES deterministic (giống tutor) để so khớp.
            var encryptedId = _encryption.Encrypt(ocrResult.Id);
            if (!string.IsNullOrEmpty(ocrResult.Id) && encryptedId != user.Identitynumber)
            {
                var isUnique = await _userRepository.IsIdentityNumberUniqueAsync(encryptedId);
                if (!isUnique)
                    throw new InvalidOperationException(
                        "CCCD này đã được sử dụng bởi tài khoản khác. Vui lòng liên hệ hỗ trợ nếu đây là nhầm lẫn.");
            }

            // 6. Đủ tuổi + CCCD hợp lệ → đánh dấu đã xác minh, ghi ngày sinh.
            // Lưu ảnh CCCD private (chỉ xem được qua signed URL). Upload mới TRƯỚC rồi mới xoá ảnh cũ:
            // nếu upload lỗi giữa chừng, DB vẫn trỏ tới ảnh cũ còn nguyên thay vì file vừa bị xoá mất.
            var previousFrontUrl = user.Idcardfronturl;
            var previousBackUrl = user.Idcardbackurl;

            user.Idcardfronturl = await _storageService.UploadPrivateFileAsync(CccdBucket, user.Userid, request.FrontImage);
            user.Idcardbackurl = await _storageService.UploadPrivateFileAsync(CccdBucket, user.Userid, request.BackImage);

            if (!string.IsNullOrEmpty(previousFrontUrl))
                await _storageService.DeleteFileAsync(CccdBucket, user.Userid, previousFrontUrl);
            if (!string.IsNullOrEmpty(previousBackUrl))
                await _storageService.DeleteFileAsync(CccdBucket, user.Userid, previousBackUrl);

            user.Isidentityverified = true;
            user.Identitynumber = encryptedId;

            // Điền hồ sơ từ CCCD. Ba trường họ tên / ngày sinh / giới tính bị KHOÁ trên giao diện sau
            // khi xác minh và gắn nhãn "đã xác minh qua CCCD", nên phải GHI ĐÈ bằng dữ liệu trên thẻ —
            // nếu chỉ điền khi trống thì không bao giờ chạy, vì học sinh bắt buộc nhập họ tên lúc hoàn
            // tất hồ sơ trước đó, dẫn tới hiển thị tên tự gõ nhưng lại dán nhãn là đã xác minh.
            // Địa chỉ thì ngược lại: giao diện vẫn cho sửa nên chỉ điền khi còn trống, tránh đè mất
            // địa chỉ hiện tại người dùng tự nhập bằng địa chỉ thường trú trên thẻ.
            user.Birthdate = dob;
            if (!string.IsNullOrWhiteSpace(ocrResult.Name))
                user.Fullname = ocrResult.Name;
            if (GenderHelper.FromEkycSex(ocrResult.Sex) is { } gender)
                user.Gender = gender;
            if (string.IsNullOrWhiteSpace(user.Address) && !string.IsNullOrWhiteSpace(ocrResult.Address))
                user.Address = ocrResult.Address;

            return new EkycVerificationResult
            {
                Ocr = ocrResult,
                DateOfBirth = dob,
                Verified = true,
                Response = new CccdUploadResponse
                {
                    OcrSuccess = true,
                    DateOfBirth = ocrResult.Dob,
                    Message = "Xác minh độ tuổi thành công."
                }
            };
        }

        // Private helpers

        /// <summary>
        /// OCR không đọc được CCCD (null/độ tin cậy thấp/không parse được ngày sinh). Dưới ngưỡng
        /// <see cref="MaxOcrAttemptsBeforeManualReview"/> lần liên tiếp: vẫn chặn như cũ, ném lỗi để
        /// người dùng tự chụp lại. ĐỦ ngưỡng: không chặn nữa — nhận và lưu luôn 2 ảnh, đánh dấu chờ
        /// Admin xem thủ công (KHÔNG đánh dấu Isidentityverified — Admin phải tự xem ảnh và quyết
        /// định qua endpoint riêng), báo cho Admin lẫn học sinh biết.
        /// </summary>
        private async Task<EkycVerificationResult> HandleOcrFailureAsync(User user, UploadCccdRequest request, string reasonMessage)
        {
            var attempts = await _userRepository.IncrementCccdOcrFailedAttemptsAsync(user.Userid);
            if (attempts < MaxOcrAttemptsBeforeManualReview)
                throw new InvalidOperationException(reasonMessage);

            _logger.LogWarning(
                "Student {UserId} OCR CCCD thất bại {Attempts} lần liên tiếp — chuyển sang chờ Admin xem thủ công.",
                user.Userid, attempts);

            // Không có dữ liệu OCR để đối chiếu tuổi/trùng CCCD — Admin sẽ tự xem ảnh và quyết định.
            user.Idcardfronturl = await _storageService.UploadPrivateFileAsync(CccdBucket, user.Userid, request.FrontImage);
            user.Idcardbackurl = await _storageService.UploadPrivateFileAsync(CccdBucket, user.Userid, request.BackImage);
            user.Isidentitypendingreview = true;
            // Đã escalate xong — reset đếm để lần xác minh KẾ TIẾP (nếu Admin từ chối, học sinh phải
            // gửi lại từ đầu) không bị cộng dồn từ loạt thất bại này.
            user.Cccdocrfailedattempts = 0;

            await NotifyAdminsOfPendingReviewAsync(user);
            await NotifyStudentOfPendingReviewAsync(user);

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
                    Title = "CCCD học sinh cần xem thủ công",
                    Message = $"Học sinh {user.Fullname ?? user.Userid} xác minh CCCD tự động thất bại nhiều lần, cần Admin xem thủ công.",
                    Type = NotificationType.IdentityReviewRequested
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifyAdminsOfPendingReviewAsync failed for student {UserId}", user.Userid);
            }
        }

        /// <summary>Báo cho chính học sinh biết ảnh CCCD của họ đã được gửi cho Admin xem thủ công —
        /// tách khỏi <see cref="CccdUploadResponse.Message"/> (chỉ hiện ngay lúc bấm nút) vì đây là
        /// thông báo bền, học sinh vẫn thấy lại được dù rời trang trước khi đọc toast.</summary>
        private async Task NotifyStudentOfPendingReviewAsync(User user)
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
                _logger.LogError(ex, "NotifyStudentOfPendingReviewAsync failed for student {UserId}", user.Userid);
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
                _logger.LogWarning(ex, "FPT.AI OCR failed for student verification.");
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
    }
}
