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
}
