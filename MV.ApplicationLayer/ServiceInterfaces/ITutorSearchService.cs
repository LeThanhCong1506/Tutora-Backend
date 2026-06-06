using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    /// <summary>
    /// Service interface for tutor search operations
    /// Provides business logic layer for searching and filtering tutors
    /// Matches Figma UI design for tutor search page
    /// </summary>
    public interface ITutorSearchService
    {
        /// <summary>
        /// Search tutors with filters and pagination
        /// </summary>
        /// <param name="parameters">Search and filter parameters</param>
        /// <returns>Paged list of tutor search results</returns>
        Task<TutorSearchPagedResponse> SearchTutorsAsync(TutorSearchParameters parameters);
    }
}
