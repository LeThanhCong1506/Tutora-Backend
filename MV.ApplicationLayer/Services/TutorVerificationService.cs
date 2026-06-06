using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.Interfaces;
using Supabase;
using System.Globalization;
using System.Text.Json;
using static MV.DomainLayer.Constants.LessonStatus;

namespace MV.ApplicationLayer.Services
{
    public partial class TutorVerificationService : ITutorVerificationService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly Client _supabaseClient;
        private readonly IFptAiService _fptAiService;
        private readonly IDistributedCache _cache;
        private readonly IAppDbContext _dbContext;
        private readonly ILogger<TutorVerificationService> _logger;

        private const string CacheKeyPrefix = "tutor_preview:";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

        private const string FullProfileCacheKeyPrefix = "tutor_full_profile:";
        private static readonly TimeSpan FullProfileCacheDuration = TimeSpan.FromMinutes(20);

        // Phase 1: Identity Verification Thresholds
        private const double OCR_PROBABILITY_THRESHOLD = 90.0;

        public TutorVerificationService(
            IUnitOfWork unitOfWork,
            Client supabaseClient,
            IFptAiService fptAiService,
            IDistributedCache cache,
            IAppDbContext dbContext,
            ILogger<TutorVerificationService> logger)
        {
            _unitOfWork = unitOfWork;
            _supabaseClient = supabaseClient;
            _fptAiService = fptAiService;
            _cache = cache;
            _dbContext = dbContext;
            _logger = logger;
        }

        /// <summary>
        /// Phase 1: Identity Verification
        /// 1. OCR CCCD để lấy thông tin
        /// 2. Kết quả: IdentityVerified = true nếu OCR > 90%
        /// </summary>
        public async Task<APIResponse<FptAiResult>> VerifyAndSaveTutorDataAsync(string userId, string frontImgPath, string backImgPath)
        {
            try
            {
                _logger.LogInformation("Starting identity verification for user {UserId}", userId);

                // 1. Lấy thông tin User từ DB
                var user = await _unitOfWork.UserRepository.GetUserByIdAsync(userId);
                if (user == null)
                {
                    return APIResponse<FptAiResult>.Fail("Không tìm thấy người dùng.", 404);
                }

                // 2. Tải ảnh mặt trước CCCD từ Supabase (Bucket Private "id-cards")
                byte[] idCardBytes;
                try
                {
                    string cleanFrontPath = GetRelativePath(frontImgPath, "id-cards");
                    idCardBytes = await _supabaseClient.Storage
                        .From("id-cards")
                        .Download(cleanFrontPath, (Supabase.Storage.TransformOptions?)null, null);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to download ID card from Supabase for user {UserId}", userId);
                    return APIResponse<FptAiResult>.Fail($"Lỗi không tải được ảnh CCCD từ Supabase: {ex.Message}", 500);
                }

                // 3. Gọi FPT.AI để quét thông tin CCCD (OCR)
                using var ocrStream = new MemoryStream(idCardBytes);
                var ocrResponse = await _fptAiService.VerifyIdCardAsync(ocrStream, "front_card.jpg");

                // 6. Validate kết quả OCR từ FPT
                if (ocrResponse == null || ocrResponse.Data == null || ocrResponse.Data.Count == 0)
                {
                    _logger.LogWarning("OCR failed for user {UserId}: No data returned", userId);
                    return APIResponse<FptAiResult>.Fail("Không nhận diện được giấy tờ. Vui lòng chụp rõ nét hơn.", 400);
                }

                var resultData = ocrResponse.Data[0];

                // Kiểm tra độ thật giả của CCCD (> 90% là an toàn)
                if (resultData.Probability < OCR_PROBABILITY_THRESHOLD)
                {
                    _logger.LogWarning("ID card authenticity check failed for user {UserId}: Probability={Probability}",
                        userId, resultData.Probability);
                    return APIResponse<FptAiResult>.Fail(
                        $"Ảnh CCCD có dấu hiệu giả mạo hoặc quá mờ/mất góc. Độ tin cậy: {resultData.Probability:F1}%", 400);
                }

                // 5. Quyết định kết quả Identity Verification (chỉ dựa vào OCR)
                bool identityVerified = resultData.Probability >= OCR_PROBABILITY_THRESHOLD;

                // 5.5. Kiểm tra trùng số CCCD (unique constraint trên DB)
                if (!string.IsNullOrEmpty(resultData.Id) && resultData.Id != user.Identitynumber)
                {
                    var isUnique = await _unitOfWork.UserRepository.IsIdentityNumberUniqueAsync(resultData.Id);
                    if (!isUnique)
                    {
                        _logger.LogWarning("Duplicate identity number detected for user {UserId}: {IdentityNumber}",
                            userId, resultData.Id);
                        return APIResponse<FptAiResult>.Fail(
                            "Số CCCD/CMND này đã được sử dụng bởi tài khoản khác. Vui lòng liên hệ hỗ trợ nếu đây là nhầm lẫn.", 409);
                    }
                }

                // 6. Cập nhật thông tin vào User Entity
                user.Identitynumber = resultData.Id;       // Số CCCD
                user.Idcardfronturl = frontImgPath;        // Link ảnh trước
                user.Idcardbackurl = backImgPath;          // Link ảnh sau
                user.Isidentityverified = identityVerified; // Đánh dấu đã xác thực

                // Lưu dữ liệu gốc
                var ekycData = new
                {
                    OcrResult = resultData,
                    VerifiedAt = MV.DomainLayer.Helpers.VietnamTimeHelper.Now
                };
                user.Ekycrawdata = JsonSerializer.Serialize(ekycData);

                // Tự động điền thông tin cá nhân (Auto-fill)
                if (string.IsNullOrEmpty(user.Fullname))
                    user.Fullname = resultData.Name;

                if (string.IsNullOrEmpty(user.Address))
                    user.Address = resultData.Address;

                if (string.IsNullOrEmpty(user.Gender))
                    user.Gender = resultData.Sex;

                if (user.Birthdate == null && !string.IsNullOrEmpty(resultData.Dob))
                {
                    if (DateOnly.TryParseExact(resultData.Dob, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dob))
                    {
                        user.Birthdate = dob;
                    }
                }

                // 7. Lưu xuống Database
                await _unitOfWork.UserRepository.UpdateUserAsync(user);
                await _unitOfWork.SaveChangesAsync();

                // 8. Tính toán Final Profile Status
                await CalculateFinalProfileStatusAsync(userId);

                _logger.LogInformation("Identity verification completed for user {UserId}: Verified={Verified}",
                    userId, identityVerified);

                return APIResponse<FptAiResult>.Success(resultData,
                    identityVerified
                        ? "Xác thực danh tính thành công!"
                        : "Xác thực danh tính hoàn tất nhưng cần được kiểm tra thêm.");
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message?.Contains("identitynumber") == true ||
                                                ex.InnerException?.Message?.Contains("users_identitynumber_key") == true)
            {
                _logger.LogWarning(ex, "Duplicate identity number constraint violation for user {UserId}", userId);
                return APIResponse<FptAiResult>.Fail(
                    "Số CCCD/CMND này đã được sử dụng bởi tài khoản khác. Vui lòng liên hệ hỗ trợ nếu đây là nhầm lẫn.", 409);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "System error during identity verification for user {UserId}", userId);
                return APIResponse<FptAiResult>.Fail($"Lỗi hệ thống: {ex.Message}", 500);
            }
        }

        /// <summary>
        /// Tính toán Final Profile Status dựa trên tất cả các điều kiện
        /// Logic mới đơn giản:
        /// - Draft: Chưa đủ required fields hoặc chưa Identity Verified
        /// - Active: Identity Verified + Đủ required fields + Certificate valid (hoặc không cần certificate)
        /// - PendingApproval: User submit cho admin review (certificate invalid)
        /// - Rejected: Admin từ chối
        /// </summary>
        public async Task<string> CalculateFinalProfileStatusAsync(string userId)
        {
            try
            {
                var user = await _unitOfWork.UserRepository.GetUserByIdAsync(userId);
                var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(userId);
                var subjects = await _unitOfWork.TutorRepository.GetTutorSubjectsByTutorIdAsync(userId);
                var prices = await _unitOfWork.TutorRepository.GetTutorSubjectGradePricesAsync(userId);

                if (user == null || profile == null)
                {
                    _logger.LogWarning("User or profile not found for status calculation: {UserId}", userId);
                    return TutorProfileStatus.Draft;
                }

                // 1. Kiểm tra Identity Verified (Phase 1)
                bool identityVerified = user.Isidentityverified ?? false;

                // 2. Kiểm tra Required Fields đã điền đủ chưa
                bool hasRequiredFields = CheckRequiredFields(profile, user, subjects, prices);

                // 3. Quyết định Final Status
                string newStatus;

                if (!identityVerified || !hasRequiredFields)
                {
                    // Chưa đủ điều kiện cơ bản
                    newStatus = TutorProfileStatus.Draft;
                }
                else
                {
                    // Identity OK + Required fields OK → Active
                    newStatus = TutorProfileStatus.Active;
                }

                // 4. Update Profile Status nếu thay đổi
                if (!string.Equals(profile.Profilestatus, newStatus, StringComparison.OrdinalIgnoreCase))
                {
                    var oldStatus = profile.Profilestatus;
                    profile.Profilestatus = newStatus;
                    profile.Updatedat = MV.DomainLayer.Helpers.VietnamTimeHelper.Now;

                    if (string.Equals(newStatus, TutorProfileStatus.Active, StringComparison.OrdinalIgnoreCase))
                    {
                        profile.Ispublic = true; // Tự động public khi active
                    }

                    await _unitOfWork.SaveChangesAsync();

                    _logger.LogInformation(
                        "Profile status updated for user {UserId}: {OldStatus} -> {NewStatus}",
                        userId, oldStatus, newStatus);
                }

                return newStatus;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating final profile status for user {UserId}", userId);
                return TutorProfileStatus.Draft;
            }
        }

        /// <summary>
        /// Kiểm tra các trường bắt buộc đã được điền chưa
        /// Yêu cầu: Giá, Môn học, Thời gian dạy, Introduction, Avatar, Video
        /// </summary>
        private bool CheckRequiredFields(
            Tutorprofile profile,
            User user,
            List<Tutorsubject>? subjects,
            List<Tutorsubjectgradeprice>? prices)
        {
            // Basic Info
            bool hasHeadline = !string.IsNullOrWhiteSpace(profile.Headline);
            bool hasTeachingArea = !string.IsNullOrWhiteSpace(profile.Teachingareacity);
            bool hasSubjects = subjects != null && subjects.Count > 0;

            // Introduction
            bool hasBio = !string.IsNullOrWhiteSpace(profile.Bio);
            bool hasEducation = !string.IsNullOrWhiteSpace(profile.Education);

            // Pricing
            bool hasHourlyRate = prices != null && prices.Any(p => p.Isactive && p.Priceperhour > 0);

            // Media
            bool hasAvatar = !string.IsNullOrWhiteSpace(user.Avatarurl);
            bool hasVideo = !string.IsNullOrWhiteSpace(profile.Videointrourl);

            return hasHeadline && hasTeachingArea && hasSubjects &&
                   hasBio && hasEducation && hasHourlyRate && hasAvatar && hasVideo;
        }

        private string GetRelativePath(string inputUrl, string bucketName)
        {
            // Nếu input không chứa http thì nó đã là path rồi -> trả về luôn
            if (string.IsNullOrEmpty(inputUrl) || !inputUrl.StartsWith("http"))
            {
                return inputUrl;
            }

            // Logic: Tìm vị trí tên bucket và lấy phần sau nó
            // URL chuẩn thường là: .../storage/v1/object/public/id-cards/folder/file.jpg
            var keyword = $"/{bucketName}/";
            int index = inputUrl.IndexOf(keyword);

            if (index >= 0)
            {
                // Lấy phần substring sau 'id-cards/'
                // Cần xử lý cả trường hợp có Query String (?token=...) nếu là signed url
                string path = inputUrl.Substring(index + keyword.Length);

                // Cắt bỏ query params nếu có
                int queryIndex = path.IndexOf('?');
                if (queryIndex >= 0)
                {
                    path = path.Substring(0, queryIndex);
                }
                return path;
            }

            return inputUrl; // Fallback
        }

        /// <summary>
        /// Get verification progress for a tutor - returns status of all sections
        /// </summary>
        public async Task<bool> UpdateTutorStatusToPendingAsync(string userId)
        {
            var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(userId);
            if (profile == null)
            {
                return false;
            }

            profile.Profilestatus = TutorProfileStatus.PendingApproval;
            profile.Updatedat = MV.DomainLayer.Helpers.VietnamTimeHelper.Now;

            await _unitOfWork.SaveChangesAsync();

            return true;
        }
    }
}
