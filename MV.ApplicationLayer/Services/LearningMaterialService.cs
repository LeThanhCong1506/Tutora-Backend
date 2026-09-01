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
    ISessionPracticeRepository practiceRepository,
    ITutorAiClient aiClient,
    ILogger<LearningMaterialService> logger) : ILearningMaterialService
{
    /// <summary>
    /// Định dạng trích được toàn văn để AI sinh bài tập. Tài liệu lớp chỉ nhận ảnh và
    /// PDF nên danh sách này phủ hết; định dạng khác vẫn upload/tải về được, chỉ là
    /// không dùng để sinh câu hỏi.
    /// </summary>
    private static readonly HashSet<string> ExtractableTypes =
        new(StringComparer.OrdinalIgnoreCase) { "pdf", "png", "jpg", "jpeg", "webp" };

    public async Task<List<LearningMaterialResponse>> GetByBookingIdAsync(int bookingId, string actorUserId)
    {
        var booking = await bookingRepository.FindWithStudentAsync(bookingId)
            ?? throw new BookingNotFoundException();

        if (!IsPartyToBooking(booking, actorUserId))
            throw new MaterialAccessDeniedException();

        var materials = await repository.GetByBookingIdAsync(bookingId);

        // Ghép trạng thái trích nội dung (1 truy vấn, không N+1) để FE biết tài liệu
        // nào đã dùng được để sinh bài tập.
        var contents = await practiceRepository.GetMaterialContentsAsync(
            materials.Select(m => m.Materialid).ToList());
        var byMaterial = contents.ToDictionary(c => c.MaterialId);

        return materials.Select(m =>
        {
            var response = MapToResponse(m);
            if (byMaterial.TryGetValue(m.Materialid, out var content))
            {
                response.ContentStatus = content.Status;
                response.PageCount = content.PageCount;
            }
            return response;
        }).ToList();
    }

    public async Task<LearningMaterialResponse> UploadAsync(
        int bookingId, string tutorUserId, IFormFile file, string title, string? description, bool isPublic)
    {
        // FindWithRelations (không phải FindWithStudent) để lấy được Subject/Gradelevel
        // — cần đối chiếu tài liệu có đúng môn đang dạy không.
        var booking = await bookingRepository.FindWithRelationsAsync(bookingId)
            ?? throw new BookingNotFoundException();

        if (booking.Tutorid != tutorUserId)
            throw new MaterialAccessDeniedException();

        var fileType = Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant();

        // Đọc bytes TRƯỚC khi upload: sau upload stream đã bị đọc tới cuối, CopyToAsync
        // lúc đó trả về mảng rỗng.
        byte[]? extractBytes = ExtractableTypes.Contains(fileType) ? await ReadAllBytesAsync(file) : null;

        AiMaterialExtraction? extraction = null;
        if (extractBytes != null)
        {
            var subject = booking.Tutorsubjectgradeprice?.Subject?.Subjectname;
            var grade = booking.Tutorsubjectgradeprice?.Gradelevel?.Gradename;

            extraction = await aiClient.ExtractMaterialAsync(extractBytes, file.FileName, subject, grade);

            if (extraction?.Relevant == false)
            {
                logger.LogInformation(
                    "Từ chối tài liệu '{Title}' cho booking {BookingId}: {Reason}",
                    title, bookingId, extraction.RejectReason);
                throw new MaterialNotRelevantException(extraction.RejectReason, subject);
            }
        }

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
            Filetype = fileType,
            Fileurl = fileUrl,
            Filesize = (int)file.Length,
            Ispublic = isPublic,
            Createdat = TimeZoneHelper.UtcNow,
        };

        repository.Add(material);
        await repository.SaveChangesAsync();

        logger.LogInformation("Tutor {TutorId} uploaded material '{Title}' for booking {BookingId}", tutorUserId, title, bookingId);

        // Đã trích ở trên rồi -> lưu luôn, không cần chạy ngầm. Nhờ vậy tài liệu dùng
        // được NGAY sau khi tải lên, gia sư không phải chờ hay tải lại trang.
        if (extraction != null)
        {
            practiceRepository.AddMaterialContent(new LearningMaterialContent
            {
                MaterialId = material.Materialid,
                FullText = extraction.FullText,
                PageCount = extraction.PageCount,
                Status = MaterialContentStatus.Ready,
                ExtractedAt = TimeZoneHelper.UtcNow,
            });
            await practiceRepository.SaveChangesAsync();
        }

        var response = MapToResponse(material);
        if (extraction != null)
        {
            response.ContentStatus = MaterialContentStatus.Ready;
            response.PageCount = extraction.PageCount;
        }
        return response;
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

    private static async Task<byte[]> ReadAllBytesAsync(IFormFile file)
    {
        // OpenReadStream() trả stream mới từ đầu file — không phụ thuộc vị trí con trỏ
        // hiện tại của IFormFile.
        using var stream = file.OpenReadStream();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }

    private static bool IsPartyToBooking(Booking booking, string actorUserId) =>
        booking.Tutorid == actorUserId
        || booking.Parentid == actorUserId
        || booking.Studentid == actorUserId
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
