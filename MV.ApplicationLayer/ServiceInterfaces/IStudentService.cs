using Microsoft.AspNetCore.Http;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    public interface IStudentService
    {
        /// <summary>
        /// All student profiles linked to a parent account.
        /// </summary>
        Task<List<StudentProfileResponse>> GetStudentsByParentIdAsync(string parentId);

        /// <summary>
        /// Single student profile — ownership-checked against the parent.
        /// </summary>
        Task<StudentProfileResponse> GetStudentByIdAsync(string studentId, string parentId);

        /// <summary>
        /// Parent creates a new student account; returns credentials (username + temp password).
        /// </summary>
        Task<StudentCredentialsResponse> CreateStudentAsync(CreateStudentRequest request, string parentId);

        /// <summary>
        /// Upload and replace a student's avatar image.
        /// </summary>
        Task<StudentProfileResponse> UpdateAvatarAsync(string studentId, IFormFile avatarFile, string parentId);

        /// <summary>
        /// Update a student's profile fields (name, grade, etc.).
        /// </summary>
        Task<StudentProfileResponse> UpdateStudentAsync(string studentId, UpdateStudentRequest request, string parentId);

        /// <summary>
        /// Delete a student account (parent only).
        /// </summary>
        Task DeleteStudentAsync(string studentId, string parentId);

        // ── Credentials ───────────────────────────────────────────────────

        /// <summary>
        /// Parent resets a student's password; returns new temporary credentials.
        /// </summary>
        Task<StudentCredentialsResponse> ResetStudentPasswordAsync(string studentId, string parentId);

        /// <summary>
        /// Student checks whether their account is linked to a parent.
        /// </summary>
        Task<StudentLinkStatusResponse> GetLinkStatusAsync(string studentUserId);

        /// <summary>
        /// Student updates their own profile fields (name, birthdate, school, grade, learning goals).
        /// </summary>
        Task<StudentProfileResponse> UpdateSelfProfileAsync(string studentUserId, UpdateStudentRequest request);

        /// <summary>
        /// Student uploads their own avatar — updates both Studentprofile and linked User record.
        /// </summary>
        Task<StudentProfileResponse> UpdateSelfAvatarAsync(string studentUserId, IFormFile avatarFile);

        // Điều kiện đặt lịch: xác minh CCCD (≥16)

        /// <summary>
        /// Học sinh tự đăng ký xác minh CCCD để chứng minh đủ 16 tuổi.
        /// </summary>
        Task<CccdUploadResponse> VerifyStudentCccdAsync(string studentUserId, UploadCccdRequest request);

        /// <summary>
        /// Trạng thái đủ điều kiện đặt lịch của học sinh
        /// </summary>
        Task<StudentBookingEligibilityResponse> GetBookingEligibilityAsync(string studentUserId);

        /// <summary>
        /// Học sinh tự đăng ký nhập/cập nhật SĐT phụ huynh (để nhận ZNS theo dõi). null/empty = xóa.
        /// </summary>
        Task<string?> SetParentPhoneAsync(string studentUserId, string? parentPhone);
    }
}
