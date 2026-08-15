using Microsoft.AspNetCore.Http;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Admin/Staff support inbox: one continuous thread per user (Tutor/Parent/Student), independent
/// of disputes. See <see cref="MV.DomainLayer.Entities.Supportthread"/>.
/// </summary>
public interface ISupportMessageService
{
    /// <summary>Conversation list for the CMS support inbox, newest activity first.</summary>
    /// <param name="role">Optional exact-match filter on the user's Primaryrole (Tutor/Parent/Student).</param>
    /// <param name="search">Optional case-insensitive match on the user's name or phone.</param>
    Task<List<SupportThreadSummaryResponse>> GetThreadsForAdminAsync(string? role, string? search);

    /// <summary>Gets (creating if needed) the thread with a user and marks it read for admin. Null if the user doesn't exist.</summary>
    Task<SupportThreadDetailResponse?> GetOrCreateThreadForAdminAsync(string userId);

    /// <summary>Sends a message from admin/staff to a user, creating the thread on first contact.</summary>
    Task<SupportMessageItemResponse?> SendMessageAsAdminAsync(string userId, string adminSenderId, string message);

    /// <summary>Uploads and sends an image from admin/staff to a user. Null if the user doesn't exist.</summary>
    Task<SupportMessageItemResponse?> SendImageAsAdminAsync(string userId, string adminSenderId, IFormFile file);

    /// <summary>The calling user's own thread with support, marked read for them. Null if no thread exists yet.</summary>
    Task<SupportThreadDetailResponse?> GetThreadForUserAsync(string userId);

    /// <summary>Sends a message from the user to admin/staff, creating the thread on first contact.</summary>
    Task<SupportMessageItemResponse> SendMessageAsUserAsync(string userId, string message);

    /// <summary>Uploads and sends an image from the user to admin/staff, creating the thread on first contact.</summary>
    Task<SupportMessageItemResponse> SendImageAsUserAsync(string userId, IFormFile file);
}
