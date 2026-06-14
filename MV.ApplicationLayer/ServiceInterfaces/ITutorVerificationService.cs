using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    public interface ITutorVerificationService
    {
        /// <summary>
        /// OCR front and back CCCD images via FPT AI, then persist verified identity data.
        /// </summary>
        Task<APIResponse<FptAiResult>> VerifyAndSaveTutorDataAsync(string userId, string frontImgPath, string backImgPath);

        /// <summary>
        /// Step-by-step verification progress for the tutor onboarding wizard.
        /// Returns <c>null</c> if the tutor profile does not exist.
        /// </summary>
        Task<VerificationProgressResponse?> GetVerificationProgressAsync(string userId);

        /// <summary>
        /// Public tutor search card — cached 15 min.
        /// Returns <c>null</c> if the tutor is not Active.
        /// </summary>
        Task<TutorProfilePreviewResponse?> GetTutorProfilePreviewAsync(string tutorId);

        /// <summary>
        /// Public tutor profile info without schedule/package data.
        /// Returns <c>null</c> if the tutor is not Active.
        /// </summary>
        Task<TutorProfileInfoResponse?> GetTutorProfileInfoAsync(string tutorId);

        /// <summary>
        /// Public tutor schedule payload including availability and packages.
        /// Returns <c>null</c> if the tutor is not Active.
        /// </summary>
        Task<TutorScheduleResponse?> GetTutorScheduleAsync(string tutorId);

        /// <summary>
        /// Full public landing page for a tutor — includes schedule, feedbacks, and active classes; cached 20 min.
        /// Returns <c>null</c> if the tutor is not Active.
        /// </summary>
        Task<TutorFullProfileResponse?> GetTutorFullProfileAsync(string tutorId);

        /// <summary>
        /// Set tutor status to Pending after profile submission, triggering admin review queue.
        /// </summary>
        Task<bool> UpdateTutorStatusToPendingAsync(string userId);
    }
}
