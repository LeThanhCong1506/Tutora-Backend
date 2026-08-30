using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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
    IServiceScopeFactory scopeFactory,
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
        var booking = await bookingRepository.FindWithStudentAsync(bookingId)
            ?? throw new BookingNotFoundException();

        if (booking.Tutorid != tutorUserId)
            throw new MaterialAccessDeniedException();

        var fileType = Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant();

        // Đọc bytes TRƯỚC khi upload: sau upload stream đã bị đọc tới cuối, CopyToAsync
        // lúc đó trả về mảng rỗng.
        byte[]? extractBytes = ExtractableTypes.Contains(fileType) ? await ReadAllBytesAsync(file) : null;

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

        // Trích toàn văn NGẦM: gia sư bấm "Tạo câu hỏi" giữa buổi dạy thì nội dung
        // phải sẵn sàng rồi, không thể bắt chờ parse file lúc đó. Upload KHÔNG chờ
        // việc này — hỏng thì material vẫn dùng để xem/tải bình thường.
        if (extractBytes != null)
            QueueContentExtraction(material.Materialid, extractBytes, file.FileName);

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

    private static async Task<byte[]> ReadAllBytesAsync(IFormFile file)
    {
        // OpenReadStream() trả stream mới từ đầu file — không phụ thuộc vị trí con trỏ
        // hiện tại của IFormFile.
        using var stream = file.OpenReadStream();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Chạy trích xuất ngoài request hiện tại. PHẢI tạo scope mới: scope của request
    /// bị dispose ngay khi response trả về, dùng lại DbContext cũ là ObjectDisposedException.
    /// </summary>
    private void QueueContentExtraction(int materialId, byte[] fileBytes, string fileName)
    {
        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var practiceRepo = scope.ServiceProvider.GetRequiredService<ISessionPracticeRepository>();
            var aiClient = scope.ServiceProvider.GetRequiredService<ITutorAiClient>();
            var scopedLogger = scope.ServiceProvider.GetRequiredService<ILogger<LearningMaterialService>>();

            // Ghi trạng thái 'processing' trước để FE biết đang chạy mà disable chọn.
            var content = new LearningMaterialContent
            {
                MaterialId = materialId,
                FullText = string.Empty,
                Status = MaterialContentStatus.Processing,
                ExtractedAt = TimeZoneHelper.UtcNow,
            };

            try
            {
                practiceRepo.AddMaterialContent(content);
                await practiceRepo.SaveChangesAsync();

                var extracted = await aiClient.ExtractMaterialAsync(fileBytes, fileName);
                if (extracted == null)
                {
                    content.Status = MaterialContentStatus.Failed;
                    content.ErrorMessage = "Không đọc được nội dung tài liệu.";
                }
                else
                {
                    content.FullText = extracted.FullText;
                    content.PageCount = extracted.PageCount;
                    content.Status = MaterialContentStatus.Ready;
                }

                content.ExtractedAt = TimeZoneHelper.UtcNow;
                await practiceRepo.SaveChangesAsync();

                scopedLogger.LogInformation("Trích nội dung tài liệu {MaterialId}: {Status}", materialId, content.Status);
            }
            catch (Exception ex)
            {
                scopedLogger.LogError(ex, "Lỗi trích nội dung tài liệu {MaterialId}", materialId);
                try
                {
                    content.Status = MaterialContentStatus.Failed;
                    content.ErrorMessage = "Lỗi hệ thống khi xử lý tài liệu.";
                    await practiceRepo.SaveChangesAsync();
                }
                catch (Exception saveEx)
                {
                    scopedLogger.LogError(saveEx, "Không ghi được trạng thái failed cho tài liệu {MaterialId}", materialId);
                }
            }
        });
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
