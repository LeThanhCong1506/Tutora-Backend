using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces
{
    public interface IRefreshTokenRepository
    {
        Task CreateAsync(RefreshToken token);
        Task<RefreshToken?> GetByTokenHashAsync(string tokenHash);
        Task RevokeAllByFamilyAsync(string tokenFamily);
        Task RevokeAllByUserIdAsync(string userId);

        /// <summary>Thu hồi đúng tập token theo Id.</summary>
        Task RevokeTokensAsync(IEnumerable<string> tokenIds);
    }
}
