using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces
{
    public interface IStudentRepository
    {
        Task<List<Studentprofile>> GetByParentIdAsync(string parentId);
        Task<Studentprofile?> GetByIdAndParentAsync(string studentId, string parentId);
        Task<Studentprofile?> FindByStudentCodeAsync(string code);
        Task<int> CountByParentIdAsync(string parentId);
        Task<int> GetMaxSeqByParentIdAsync(string parentId, string prefix);
        Task CreateAsync(Studentprofile student);
        void Update(Studentprofile student);
        void SoftDelete(Studentprofile student);
        Task<bool> HasActiveBookingAsync(string studentId);
        Task<bool> IsStudentIdUniqueAsync(string id);
        Task<bool> IsStudentCodeUniqueAsync(string code);

        /// <summary>
        /// Generate a unique Studentid in the canonical format STU-{10 hex chars uppercase}.
        /// Retries on collision; throws if cannot find a unique id after several attempts.
        /// </summary>
        Task<string> GenerateUniqueStudentIdAsync();
        Task<List<string>> GetAllStudentCodesAsync();
        Task<List<Studentprofile>> GetByLinkedUserIdAsync(string linkedUserId);
        Task<Studentprofile?> FindByStudentOrLinkedUserAsync(string userId);
        Task<List<string>> GetStudentIdsByParentIdAsync(string parentId);
    }
}
