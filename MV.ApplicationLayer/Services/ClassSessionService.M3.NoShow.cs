using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using static MV.DomainLayer.Constants.ClassSessionStatus;

namespace MV.ApplicationLayer.Services;

public partial class ClassSessionService
{
    // ── M3-T7: No-show Handling ───────────────────────────────────────────────

    /// <summary>
    /// Gia sư còn trong thời gian được coi là "có thể đang vào lớp trễ" — báo vắng mặt chỉ mở
    /// sau khi buổi đã trễ chừng này so với giờ bắt đầu (khôi phục lại ràng buộc gốc, bị gỡ ở
    /// commit 7f509cf theo một quyết định sản phẩm hoá ra lại cho báo vắng mặt được cả TRƯỚC giờ học).
    /// </summary>
    public const int NoShowReportEarliestMinutes = 15;

    public async Task<ClassSessionDetailResponse> ReportTutorNoShowAsync(int classSessionId, string userId, string role, ReportNoShowRequest? request = null)
    {
        var studentIds = role == UserRole.Parent
            ? await _context.Studentprofiles.Where(s => s.Parentid == userId).Select(s => s.Studentid).ToListAsync()
            : await _context.Studentprofiles.Where(s => s.Studentid == userId || s.Linkeduserid == userId).Select(s => s.Studentid).ToListAsync();

        var ownedSession = await _context.ClassSessions
            .AsNoTracking()
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId && studentIds.Contains(l.Studentid!))
            ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học hoặc bạn không có quyền truy cập", 404);

        if (role == UserRole.Student)
        {
            var studentProfile = await _context.Studentprofiles.FirstOrDefaultAsync(s => s.Studentid == userId || s.Linkeduserid == userId);
            if (studentProfile != null && studentProfile.Parentid != null)
                throw new ClassSessionException(BookingErrorCodes.StudentManagedByParent, "Tài khoản học sinh do phụ huynh quản lý không thể tự báo cáo vắng mặt", 403);
        }

        // Fast-fail trước khi upload evidence (UX) — nguồn sự thật thật sự là re-check sau khi
        // lock bên dưới, vì đọc không lock ở đây có thể đã cũ (stale) so với lúc ghi thật.
        if (ownedSession.Status != Scheduled)
            throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Buổi học không ở trạng thái đã lên lịch", 400);
        if (DisputeSettlementPolicy.IsTerminalBooking(ownedSession.Booking?.Status))
            throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Booking đã kết thúc, không thể tạo báo cáo mới", 400);

        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

        if ((now - ownedSession.Scheduledstart).TotalMinutes < NoShowReportEarliestMinutes)
            throw new ClassSessionException(
                ClassSessionErrorCodes.TooEarlyToReportNoShow,
                $"Chỉ có thể báo cáo vắng mặt sau {NoShowReportEarliestMinutes} phút kể từ giờ bắt đầu",
                400);

        // Reported time is advisory context folded into the dispute reason text — it's not
        // compared against Scheduledstart; only the real server clock (now, gated above) decides
        // when reporting opens.
        var reportedAt = request?.ReportedAt ?? now;
        var reasonText = !string.IsNullOrWhiteSpace(request?.Reason)
            ? $"Tutor no-show lúc {reportedAt:dd/MM/yyyy HH:mm}: {request!.Reason}"
            : $"Tutor no-show: Gia sư không có mặt lúc {reportedAt:dd/MM/yyyy HH:mm}";

        var uploadedEvidence = new List<string>();
        var evidenceFolder = $"dispute-evidence-{classSessionId}";
        Dispute dispute;
        ClassSession classSession;

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            if (request?.Files?.Count > 0)
            {
                await _storageService.EnsureBucketExistsAsync(ClassSessionAttachmentBucket);
                foreach (var file in request.Files.Where(file => file is { Length: > 0 }))
                {
                    uploadedEvidence.Add(await _storageService.UploadFileAsync(
                        ClassSessionAttachmentBucket,
                        evidenceFolder,
                        file));
                }
            }

            // Lock + re-check ngay trước khi ghi Status — tránh đè lên claim "chỉ mình gia sư có
            // mặt" nếu SubmitReportAsync (nhánh solo tutor no-show) vừa commit trước trên đúng
            // buổi này (2 actor có thể cùng nhắm vào field Status của 1 session không có
            // concurrency token, xem ClassSessionService.M3.Attendance.cs).
            classSession = await ClassSessionLockHelper.LockById(_context, classSessionId)
                .Include(l => l.Booking)
                .SingleOrDefaultAsync()
                ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học", 404);

            if (classSession.Status != Scheduled)
                throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Buổi học không còn ở trạng thái đã lên lịch (có thể vừa được xử lý bởi luồng khác)", 400);
            if (DisputeSettlementPolicy.IsTerminalBooking(classSession.Booking?.Status))
                throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Booking đã kết thúc, không thể tạo báo cáo mới", 400);

            classSession.Status = NoShow;
            classSession.Istutorpresent = false;

            // Auto-create dispute record to track no-show, including evidence in the same request.
            dispute = new Dispute
            {
                Classsessionid = classSessionId,
                Bookingid = classSession.Bookingid,
                Createdby = userId,
                Disputetype = DisputeTypes.NoShow,
                Reason = reasonText,
                Status = DisputeStatus.Pending,
                Evidence = uploadedEvidence.Count > 0 ? JsonSerializer.Serialize(uploadedEvidence) : null,
                Createdat = now
            };
            _context.Disputes.Add(dispute);

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            foreach (var fileUrl in uploadedEvidence)
            {
                try
                {
                    await _storageService.DeleteFileAsync(
                        ClassSessionAttachmentBucket,
                        evidenceFolder,
                        fileUrl);
                }
                catch (Exception cleanupError)
                {
                    _logger.LogWarning(
                        cleanupError,
                        "Failed to clean orphan no-show evidence {FileUrl} for classSession {ClassSessionId}",
                        fileUrl,
                        classSessionId);
                }
            }

            throw;
        }

        try
        {
            var jobId = _backgroundJobClient.Enqueue<IDisputeService>(
                s => s.ClassifyDisputePriorityAsync(dispute.Disputeid, "system", true));
            _logger.LogInformation(
                "Enqueued Hangfire job {JobId} to classify priority for dispute {DisputeId}",
                jobId,
                dispute.Disputeid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue priority classification job for dispute {DisputeId}", dispute.Disputeid);
        }

        // Notify tutor about the no-show report
        if (!string.IsNullOrEmpty(classSession.Tutorid))
        {
            try
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = classSession.Tutorid,
                    Title = "Báo cáo vắng mặt",
                    Message = $"Bạn đã bị báo cáo vắng mặt cho buổi học #{classSessionId}.",
                    Type = NotificationType.LessonNoShow,
                    Referenceid = classSessionId.ToString()
                });
            }
            catch (Exception notificationError)
            {
                _logger.LogWarning(
                    notificationError,
                    "No-show dispute {DisputeId} was created but tutor notification failed",
                    dispute.Disputeid);
            }
        }

        _logger.LogInformation("User {UserId} ({Role}) reported tutor no-show for classSession {ClassSessionId}, dispute {DisputeId} created", userId, role, classSessionId, dispute.Disputeid);
        return MapToClassSessionDetailResponse(classSession);
    }

}
