using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces
{
    public interface IRefreshTokenRepository
    {
        Task CreateAsync(RefreshToken token);
        Task<RefreshToken?> GetByTokenHashAsync(string tokenHash);
        Task RevokeAllByFamilyAsync(string tokenFamily);
        Task RevokeAllByUserIdAsync(string userId);

        /// <summary>Thu hồi đúng tập token theo Id (dùng để đá 1 phiên web cụ thể — xem WebSessionTracker).</summary>
        Task RevokeTokensAsync(IEnumerable<string> tokenIds);
    }
}
