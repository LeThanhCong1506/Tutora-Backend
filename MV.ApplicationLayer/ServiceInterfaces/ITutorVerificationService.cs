using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    public interface ITutorVerificationService
    {
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
        Task<TutorProfileInfoResponse?> GetTutorProfileInfoAsync(string tutorId, bool publicView = true);

        /// <summary>
        /// Public tutor schedule payload including availability and packages.
        /// Returns <c>null</c> if the tutor is not Active.
        /// </summary>
        Task<TutorScheduleResponse?> GetTutorScheduleAsync(string tutorId, bool publicView = true);

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
