using Microsoft.AspNetCore.Http;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    public interface ITutorService
    {
        /// <summary>
        /// Tutor's own full editable profile (bio, education, pricing, subjects).
        /// </summary>
        Task<TutorProfileResponse?> GetTutorProfileAsync(string tutorId);

        /// <summary>
        /// Upload and replace the tutor's profile avatar image.
        /// </summary>
        Task<string?> UpdateTutorAvatarAsync(string userId, IFormFile avatarFile);

        /// <summary>
        /// Upload CCCD (citizen ID card) front and back images to Cloudinary.
        /// Saves the resulting URLs to user.Idcardfronturl and user.Idcardbackurl.
        /// </summary>
        Task<CccdUploadResponse> UploadCccdImagesAsync(string userId, UploadCccdRequest request);

        /// <summary>
        /// Update basic tutor info: headline, teaching area, teaching mode.
        /// Returns <see cref="ProfileUpdateOutcome.PendingApproval"/> instead of applying the change
        /// directly when the profile is already Active (needs Admin re-approval — see RequiresApprovalForEdits).
        /// </summary>
        Task<ProfileUpdateOutcome> UpdateTutorBasicInfoAsync(string userId, UpdateTutorBasicInfoRequest request);

        /// <summary>
        /// Replace the tutor's subject list in one call.
        /// </summary>
        Task<bool> UpdateTutorSubjectsAsync(string userId, UpdateTutorSubjectsRequest request);

        /// <summary>
        /// Upload or replace the tutor's intro video URL.
        /// </summary>
        Task<bool> UpdateTutorVideoAsync(string userId, UpdateTutorVideoRequest request);

        /// <summary>
        /// Update tutor bio, education, GPA, and experience sections.
        /// Returns <see cref="ProfileUpdateOutcome.PendingApproval"/> instead of applying the change
        /// directly when the profile is already Active (needs Admin re-approval — see RequiresApprovalForEdits).
        /// </summary>
        Task<ProfileUpdateOutcome> UpdateTutorIntroductionAsync(string userId, UpdateTutorIntroductionRequest request);

        // ── Certificates ──────────────────────────────────────────────────

        /// <summary>
        /// Upload a new certificate with OCR auto-validation.
        /// </summary>
        Task<CertificateUploadResponse> AddCertificateAsync(string tutorId, AddCertificateRequest request);

        /// <summary>
        /// All certificates for a tutor ordered by issue date.
        /// </summary>
        Task<List<CertificateResponse>> GetCertificatesAsync(string tutorId);

        /// <summary>
        /// Update an existing certificate's metadata or file.
        /// </summary>
        Task<CertificateResponse?> UpdateCertificateAsync(string tutorId, string certificateId, UpdateCertificateRequest request);

        /// <summary>
        /// Delete a certificate (only owner can delete).
        /// </summary>
        Task<bool> DeleteCertificateAsync(string tutorId, string certificateId);

        /// <summary>
        /// Admin: danh sách chứng chỉ có lọc/tìm kiếm/phân trang.
        /// </summary>
        Task<PagedList<PendingCertificateResponse>> GetAdminCertificatesAsync(CertificateParameters parameters);

        // ── Pricing ───────────────────────────────────────────────────────

        /// <summary>
        /// Tutor's current hourly rate, trial classSession price, and negotiation flag.
        /// </summary>
        Task<TutorPricingResponse?> GetTutorPricingAsync(string tutorId);

        /// <summary>
        /// Update tutor pricing fields.
        /// Returns <see cref="ProfileUpdateOutcome.PendingApproval"/> instead of applying the change
        /// directly when the profile is already Active (needs Admin re-approval — see RequiresApprovalForEdits).
        /// </summary>
        Task<ProfileUpdateOutcome> UpdateTutorPricingAsync(string tutorId, UpdateTutorPricingRequest request);

        /// <summary>
        /// Add a single subject-grade-price entry for tutor.
        /// </summary>
        Task<TutorSubjectGradePriceResponse> AddSubjectGradePriceAsync(string tutorId, TutorSubjectGradePriceRequest request);

        Task<bool> DeleteSubjectGradePriceAsync(string tutorId, int subjectId, int gradeLevelId);

        Task<List<TutorPackageResponse>> GetTutorPackagesAsync(string tutorId, bool includeInactive = false);

        Task<TutorPackageResponse?> CreateTutorPackageAsync(string tutorId, CreateTutorPackageRequest request);

        Task<TutorPackageResponse?> UpdateTutorPackageAsync(string tutorId, int packageId, CreateTutorPackageRequest request);

        Task<bool> DeactivateTutorPackageAsync(string tutorId, int packageId);
        Task<bool> ActivateTutorPackageAsync(string tutorId, int packageId);
        Task<bool> DeleteTutorPackageAsync(string tutorId, int packageId);

        // ── Profile submission ─────────────────────────────────────────────

        /// <summary>
        /// Returns completion status for each of the 6 required profile sections.
        /// FE dùng để hiển thị progress bar (1/6, 2/6...).
        /// </summary>
        Task<ProfileCompletionResponse> GetProfileCompletionAsync(string tutorId);

        /// <summary>
        /// Kiểm tra nếu đủ 6/6 mục và status cho phép → tự động chuyển sang PendingApproval.
        /// Được gọi tự động sau mỗi lần Tutor cập nhật một trong 6 mục.
        /// </summary>
        Task TryAutoSubmitAsync(string tutorId);

        // ── Admin certificate management ───────────────────────────────────

        /// <summary>
        /// Admin duyệt hoặc từ chối một chứng chỉ của gia sư.
        /// Nếu duyệt: set Verified + re-evaluate profile status (có thể kích hoạt Active).
        /// Nếu từ chối: set Rejected + gửi notification cho gia sư.
        /// </summary>
        Task<AdminVerifyCertificateResponse> AdminVerifyCertificateAsync(
            string tutorId, string certId, AdminVerifyCertificateRequest request, string adminId);

        /// <summary>Tutor tự bật/tắt nhận booking mới.</summary>
        Task<bool> SetAcceptingBookingsAsync(string userId, bool accepting);

        // ── Admin: profile update requests (Tutor Active chỉnh sửa hồ sơ) ───

        /// <summary>
        /// Danh sách các bản chỉnh sửa hồ sơ đang chờ Admin duyệt (lưu trong Redis),
        /// kèm giá trị hiện tại trên Tutorprofile để FE hiển thị diff.
        /// </summary>
        Task<List<PendingProfileUpdateRequestResponse>> GetPendingProfileUpdateRequestsAsync();

        /// <summary>
        /// Bản mới nhất của 1 request đang chờ duyệt (null nếu không còn). Dùng để FE tự kiểm
        /// tra dữ liệu có bị thay đổi so với lúc mở lên xem hay không, ngay trước khi Duyệt/Từ chối.
        /// </summary>
        Task<PendingProfileUpdateRequestResponse?> GetProfileUpdateRequestDetailAsync(string tutorId);

        /// <summary>
        /// Admin duyệt hoặc từ chối bản chỉnh sửa hồ sơ đang chờ của 1 tutor.
        /// Duyệt: copy field từ Redis vào Tutorprofile thật rồi xoá bản chờ.
        /// Từ chối: xoá bản chờ, Tutorprofile không đổi.
        /// Throws KeyNotFoundException nếu không còn bản chờ duyệt cho tutor này.
        /// </summary>
        Task<ReviewProfileUpdateResponse> ReviewProfileUpdateRequestAsync(
            string tutorId, AdminReviewProfileUpdateRequest request, string adminId);
    }
}
