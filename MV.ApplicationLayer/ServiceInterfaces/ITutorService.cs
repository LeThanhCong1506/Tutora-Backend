using Microsoft.AspNetCore.Http;
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
        Task<bool> UpdateTutorAvatarAsync(string userId, IFormFile avatarFile);

        /// <summary>
        /// Update basic tutor info: headline, teaching area, teaching mode.
        /// </summary>
        Task<bool> UpdateTutorBasicInfoAsync(string userId, UpdateTutorBasicInfoRequest request);

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
        /// </summary>
        Task<bool> UpdateTutorIntroductionAsync(string userId, UpdateTutorIntroductionRequest request);

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

        // ── Pricing ───────────────────────────────────────────────────────

        /// <summary>
        /// Tutor's current hourly rate, trial lesson price, and negotiation flag.
        /// </summary>
        Task<TutorPricingResponse?> GetTutorPricingAsync(string tutorId);

        /// <summary>
        /// Update tutor pricing fields.
        /// </summary>
        Task<bool> UpdateTutorPricingAsync(string tutorId, UpdateTutorPricingRequest request);

        Task<List<TutorPackageResponse>> GetTutorPackagesAsync(string tutorId, bool includeInactive = false);

        Task<TutorPackageResponse?> CreateTutorPackageAsync(string tutorId, CreateTutorPackageRequest request);

        Task<bool> DeactivateTutorPackageAsync(string tutorId, int packageId);

        // ── Profile submission ─────────────────────────────────────────────

        /// <summary>
        /// Tutor submits their completed profile for admin review.
        /// Sets status to Pending and notifies admins.
        /// </summary>
        Task<bool> SubmitForAdminReviewAsync(string tutorId);
    }
}
