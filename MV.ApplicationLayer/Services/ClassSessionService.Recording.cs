using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Helpers;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.Services;

public partial class ClassSessionService
{
    private static readonly Regex DriveFileIdPattern = new(@"/file/d/([^/]+)/", RegexOptions.Compiled);
    private static readonly TimeSpan RecordingTokenLifetime = TimeSpan.FromMinutes(15);

    public async Task<ClassSessionRecordingResponse?> GetClassSessionRecordingAsync(int classSessionId, string userId, bool isParent)
    {
        var classSession = await _context.ClassSessions
            .Where(s => s.Classsessionid == classSessionId)
            .Select(s => new { s.Tutorid, s.Studentid, s.Recordingurl, s.Recordings3key, s.Recordingsid, s.Checkouttime })
            .FirstOrDefaultAsync();
        if (classSession == null) return null;

        if (isParent)
        {
            var studentIds = await _studentRepo.GetStudentIdsByParentIdAsync(userId);
            if (classSession.Studentid == null || !studentIds.Contains(classSession.Studentid))
                return null;
        }
        else
        {
            // Tutorid trùng thẳng User.Userid (Tutorprofile dùng chung khóa với Users), nhưng
            // Studentid là khóa chính riêng của Studentprofile — khác Linkeduserid/User.Userid,
            // nên phải resolve qua profile trước khi so sánh (giống StudentClassSessionController).
            var isTutorMatch = classSession.Tutorid == userId;
            var studentProfile = isTutorMatch ? null : await _studentRepo.FindByStudentOrLinkedUserAsync(userId);
            var isStudentMatch = studentProfile != null && classSession.Studentid == studentProfile.Studentid;
            if (!isTutorMatch && !isStudentMatch)
                return null;
        }

        var (status, url) = RecordingStatusResolver.Resolve(classSession.Recordingurl, classSession.Recordings3key, classSession.Recordingsid, classSession.Checkouttime.HasValue);

        var response = new ClassSessionRecordingResponse
        {
            ClassSessionId = classSessionId,
            Status = status,
            Available = url != null
        };

        if (url != null)
        {
            var token = _recordingAccessTokenService.Issue(classSessionId, userId, RecordingTokenLifetime);
            response.StreamUrl = $"/api/class-sessions/{classSessionId}/recording/stream?token={Uri.EscapeDataString(token)}";
        }

        return response;
    }

    /// <summary>
    /// Toàn bộ chuỗi buổi liên kết chứa <paramref name="classSessionId"/> (buổi bù/buổi phụ/buổi học
    /// lại — mọi loại đều tái dùng <c>Originalsessionid</c>), kèm trạng thái ghi hình riêng từng buổi.
    /// Đi lùi tới buổi gốc của chuỗi rồi đi tới hết mọi buổi con (đệ quy, không chỉ 1 bước như
    /// <c>DisputeService.GetDisputeRecordingAsync</c>) — vì 1 buổi phụ/buổi bù sau đó vẫn có thể bị
    /// tranh chấp và sinh ra buổi học lại của chính nó, tạo chuỗi dài hơn 2.
    /// </summary>
    public async Task<List<ClassSessionRecordingChainItem>?> GetClassSessionRecordingChainAsync(int classSessionId, string userId, bool isParent)
    {
        var target = await _context.ClassSessions
            .Where(s => s.Classsessionid == classSessionId)
            .Select(s => new { s.Tutorid, s.Studentid, s.Bookingid })
            .FirstOrDefaultAsync();
        if (target == null) return null;

        if (isParent)
        {
            var studentIds = await _studentRepo.GetStudentIdsByParentIdAsync(userId);
            if (target.Studentid == null || !studentIds.Contains(target.Studentid))
                return null;
        }
        else
        {
            var isTutorMatch = target.Tutorid == userId;
            var studentProfile = isTutorMatch ? null : await _studentRepo.FindByStudentOrLinkedUserAsync(userId);
            var isStudentMatch = studentProfile != null && target.Studentid == studentProfile.Studentid;
            if (!isTutorMatch && !isStudentMatch)
                return null;
        }

        var items = await ClassSessionRecordingChainHelper.GetChainAsync(_context, classSessionId)
            ?? [];

        foreach (var item in items)
        {
            item.IsCurrent = item.ClassSessionId == classSessionId;
            if (item.Available)
            {
                var token = _recordingAccessTokenService.Issue(item.ClassSessionId, userId, RecordingTokenLifetime);
                item.StreamUrl = $"/api/class-sessions/{item.ClassSessionId}/recording/stream?token={Uri.EscapeDataString(token)}";
            }
        }

        return items;
    }

    public async Task<string?> GetRecordingDriveFileIdAsync(int classSessionId)
    {
        var url = await _context.ClassSessions
            .Where(s => s.Classsessionid == classSessionId)
            .Select(s => s.Recordingurl)
            .FirstOrDefaultAsync();

        if (string.IsNullOrEmpty(url)) return null;

        var match = DriveFileIdPattern.Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Bản rút gọn của <see cref="GetClassSessionRecordingChainAsync"/> dùng riêng cho pipeline AI
    /// tóm tắt chuỗi (ClassSessionVideoAiService) — không cần token stream, không cần userId/isParent
    /// vì caller đã xác thực quyền trên <paramref name="classSessionId"/> đầu vào rồi, và mọi buổi
    /// trong chuỗi luôn cùng Bookingid nên chắc chắn cùng 1 học sinh/gia sư.
    /// </summary>
    public Task<List<ClassSessionRecordingChainItem>?> GetClassSessionAiChainAsync(int classSessionId)
        => ClassSessionRecordingChainHelper.GetChainAsync(_context, classSessionId);
}
