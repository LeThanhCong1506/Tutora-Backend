using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services;

public class LearningMaterialService(
    ILearningMaterialRepository repository,
    IBookingRepository bookingRepository,
    IFileStorageService storageService,
    ILogger<LearningMaterialService> logger) : ILearningMaterialService
{
    public async Task<List<LearningMaterialResponse>> GetByBookingIdAsync(int bookingId, string actorUserId)
    {
        var booking = await bookingRepository.FindWithStudentAsync(bookingId)
            ?? throw new BookingNotFoundException();

        if (!IsPartyToBooking(booking, actorUserId))
            throw new MaterialAccessDeniedException();

        var materials = await repository.GetByBookingIdAsync(bookingId);
        return materials.Select(MapToResponse).ToList();
    }

    public async Task<LearningMaterialResponse> UploadAsync(
        int bookingId, string tutorUserId, IFormFile file, string title, string? description, bool isPublic)
    {
        var booking = await bookingRepository.FindWithStudentAsync(bookingId)
            ?? throw new BookingNotFoundException();

        if (booking.Tutorid != tutorUserId)
            throw new MaterialAccessDeniedException();

        await storageService.EnsureBucketExistsAsync(StorageBucket.LearningMaterials);
        var folderPath = $"booking-{bookingId}";
        var fileUrl = await storageService.UploadFileAsync(StorageBucket.LearningMaterials, folderPath, file);

        var material = new Learningmaterial
        {
            Bookingid = bookingId,
            Studentid = booking.Studentid,
            Uploadedby = tutorUserId,
            Ownertype = "tutor",
            Title = title,
            Description = description,
            Filetype = Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant(),
            Fileurl = fileUrl,
            Filesize = (int)file.Length,
            Ispublic = isPublic,
            Createdat = TimeZoneHelper.UtcNow,
        };

        repository.Add(material);
        await repository.SaveChangesAsync();

        logger.LogInformation("Tutor {TutorId} uploaded material '{Title}' for booking {BookingId}", tutorUserId, title, bookingId);

        return MapToResponse(material);
    }

    public async Task<LearningMaterialResponse> UpdateVisibilityAsync(int bookingId, int materialId, string tutorUserId, bool isPublic)
    {
        var material = await repository.GetByIdAsync(materialId)
            ?? throw new MaterialNotFoundException();

        if (material.Bookingid != bookingId)
            throw new MaterialNotFoundException();

        if (material.Uploadedby != tutorUserId)
            throw new MaterialAccessDeniedException();

        material.Ispublic = isPublic;
        await repository.SaveChangesAsync();

        return MapToResponse(material);
    }

    public async Task DeleteAsync(int bookingId, int materialId, string tutorUserId)
    {
        var material = await repository.GetByIdAsync(materialId)
            ?? throw new MaterialNotFoundException();

        if (material.Bookingid != bookingId)
            throw new MaterialNotFoundException();

        if (material.Uploadedby != tutorUserId)
            throw new MaterialAccessDeniedException();

        await storageService.DeleteFileAsync(StorageBucket.LearningMaterials, $"booking-{bookingId}", material.Fileurl);

        repository.Remove(material);
        await repository.SaveChangesAsync();

        logger.LogInformation("Tutor {TutorId} deleted material {MaterialId}", tutorUserId, materialId);
    }

    private static bool IsPartyToBooking(Booking booking, string actorUserId) =>
        booking.Tutorid == actorUserId
        || booking.Parentid == actorUserId
        || booking.Student?.Linkeduserid == actorUserId;

    private static LearningMaterialResponse MapToResponse(Learningmaterial m) => new()
    {
        MaterialId = m.Materialid,
        StudentId = m.Studentid,
        BookingId = m.Bookingid,
        UploadedBy = m.Uploadedby,
        OwnerType = m.Ownertype,
        Title = m.Title,
        Description = m.Description,
        FileType = m.Filetype,
        FileUrl = m.Fileurl,
        FileSize = m.Filesize,
        IsPublic = m.Ispublic,
        CreatedAt = m.Createdat,
    };
}
