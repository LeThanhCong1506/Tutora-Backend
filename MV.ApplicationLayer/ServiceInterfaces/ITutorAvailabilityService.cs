using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    public interface ITutorAvailabilityService
    {
        /// <summary>
        /// Add a new availability slot for a tutor
        /// </summary>
        Task<TutorAvailabilityResponse> AddAvailabilityAsync(string tutorId, CreateAvailabilityRequest request);

        /// <summary>
        /// Add multiple availability slots for a tutor at once
        /// </summary>
        Task<List<TutorAvailabilityResponse>> BulkAddAvailabilitiesAsync(string tutorId, BulkCreateAvailabilityRequest request);

        /// <summary>
        /// Get all availability slots for a tutor, sorted by dayofweek and starttime
        /// </summary>
        Task<List<TutorAvailabilityResponse>> GetAvailabilitiesAsync(string tutorId);

        /// <summary>
        /// Delete an availability slot (only owner can delete)
        /// </summary>
        Task<bool> DeleteAvailabilityAsync(string tutorId, int availabilityId);

        /// <summary>
        /// Delete multiple availability slots at once (only owner can delete)
        /// </summary>
        Task<int> BulkDeleteAvailabilitiesAsync(string tutorId, BulkDeleteAvailabilityRequest request);
    }
}
