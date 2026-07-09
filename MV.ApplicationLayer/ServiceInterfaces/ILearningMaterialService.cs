using Microsoft.AspNetCore.Http;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface ILearningMaterialService
{
    Task<List<LearningMaterialResponse>> GetByBookingIdAsync(int bookingId, string actorUserId);
    Task<LearningMaterialResponse> UploadAsync(int bookingId, string tutorUserId, IFormFile file, string title, string? description, bool isPublic);
    Task DeleteAsync(int bookingId, int materialId, string tutorUserId);
}
