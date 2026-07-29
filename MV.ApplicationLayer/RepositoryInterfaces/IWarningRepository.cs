using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces;

public interface IWarningRepository
{
    // Userwarnings
    void AddWarning(Userwarning warning);
    Task<List<Userwarning>> GetRecentWarningsAsync(string userId, DateTime since);
    Task<List<Userwarning>> GetAllWarningsAsync(string userId);

    // Profilesuspensions
    Task<bool> HasActiveSuspensionAsync(string userId);
    Task<int> CountRecentSuspensionsAsync(string userId, string suspensionType, DateTime since);
    Task<Profilesuspension?> GetActiveSuspensionAsync(string userId);

    /// <summary>
    /// Full suspension history for one user — active and past — newest first.
    /// </summary>
    Task<List<Profilesuspension>> GetUserSuspensionsAsync(string userId);

    Task<List<Profilesuspension>> GetExpiredActiveSuspensionsAsync(DateTime now);
    Task<(IReadOnlyList<Profilesuspension> Items, int Total)> GetActiveSuspensionsPagedAsync(int page, int pageSize);
    void AddSuspension(Profilesuspension suspension);

    // Tutorprofile helper (needed for suspension side-effects)
    Task<Tutorprofile?> GetTutorProfileAsync(string userId);

    Task<int> SaveChangesAsync();
}
