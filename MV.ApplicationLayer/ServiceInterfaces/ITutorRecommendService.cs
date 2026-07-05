using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    /// <summary>
    /// Service that orchestrates SQL hard-filter → AI ranking → profile fetch for tutor recommendations.
    /// </summary>
    public interface ITutorRecommendService
    {
        /// <summary>
        /// Return AI-ranked tutor recommendations for a given request.
        /// </summary>
        Task<TutorRecommendResponse> RecommendAsync(
            TutorRecommendRequest request,
            CancellationToken cancellationToken = default);
    }
}
