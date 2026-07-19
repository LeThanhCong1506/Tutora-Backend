using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces
{
    public interface IUserRepository
    {
        // Read
        Task<PagedList<User>> GetUsersAsync(UserParameters parameters);
        Task<User?> GetUserByIdAsync(string userId);
        Task<User?> GetUserByEmailAsync(string email);
        Task<User?> GetUserByUsernameAsync(string username);
        Task<PagedList<User>> GetUsersByRoleAsync(string roleName, UserParameters parameters);
        Task<PagedList<User>> GetTutorsBySubjectAsync(int subjectId, UserParameters parameters);
        Task<PagedList<User>> GetPendingTutorsAsync(UserParameters parameters);
        Task<List<Studentprofile>> GetStudentProfilesByParentIdAsync(string parentId);
        Task<Tutorprofile?> GetTutorProfileByIdAsync(string tutorId);
        Task<string?> GetUserRoleByIdAsync(string userId);
        Task<User?> GetUserByPhoneAsync(string phone);

        // Validation helpers
        Task<bool> IsEmailUniqueAsync(string email);
        Task<bool> IsUsernameUniqueAsync(string username);
        Task<bool> IsPhoneUniqueAsync(string phone);
        Task<bool> IsIdentityNumberUniqueAsync(string identityNumber);
        Task<bool> SubjectExistsAsync(int subjectId);
        Task<bool> HasTutorAlreadySelectedSubjectAsync(string tutorId, int subjectId);
        Task<bool> CheckIfTutorHasAnySubjectAsync(string tutorId);
        Task<bool> CheckIfTutorHasAnyAvailabilityAsync(string tutorId);
        Task<bool> CheckIfUserLoginCorrectAsync(string email, string password);
        Task<bool> CheckIfUserLoginCorrectByPhoneAsync(string phone, string password);
        Task<bool> CheckIfUserLoginCorrectByUsernameAsync(string username, string password);
        Task<User?> GetUserByParentCodeAsync(string parentCode);
        Task<User?> GetUserByZaloIdAsync(string zaloUserId);

        // Create
        Task CreateUserAsync(User user);
        Task CreateTutorSubjectAsync(Tutorsubject tutorSubject);
        Task CreateNewAvailabilitySlotsAsync(List<Tutoravailability> newSlots);


        // Update
        Task UpdateUserAsync(User user);
        Task UpdateTutorProfileAsync(Tutorprofile profile);
        Task UpdateLastLoginAtAsync(string userId, DateTime time);
        /// <summary>
        /// Advances last-seen only when <paramref name="time"/> is newer than
        /// the stored value, making delayed disconnect cleanup retry-safe.
        /// </summary>
        Task UpdateLastSeenAtAsync(string userId, DateTime time);

        /// <summary>
        /// Update (or clear when token is empty) the FCM device token for the given user.
        /// Returns false when the user does not exist.
        /// </summary>
        Task<bool> UpdateFcmTokenAsync(string userId, string fcmToken);



        // Delete (Soft Delete)
        Task DeleteUserAsync(User user);
        Task DeleteAllExistingAvailabilityByTutorIdAsync(string tutorId);

        // Tracking helpers (optional but good for optimizations)
        void Detach(User user);
    }
}
