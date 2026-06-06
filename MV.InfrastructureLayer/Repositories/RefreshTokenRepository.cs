using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using MV.ApplicationLayer.RepositoryInterfaces;

namespace MV.InfrastructureLayer.Repositories
{
    public class RefreshTokenRepository : IRefreshTokenRepository
    {
        private readonly AgoraDbContext _context;

        public RefreshTokenRepository(AgoraDbContext context)
        {
            _context = context;
        }

        public async Task CreateAsync(RefreshToken token)
        {
            await _context.Refreshtokens.AddAsync(token);
        }

        public async Task<RefreshToken?> GetByTokenHashAsync(string tokenHash)
        {
            return await _context.Refreshtokens
                .FirstOrDefaultAsync(t => t.Tokenhash == tokenHash);
        }

        public async Task RevokeAllByFamilyAsync(string tokenFamily)
        {
            var tokens = await _context.Refreshtokens
                .Where(t => t.Tokenfamily == tokenFamily && t.Revokedat == null)
                .ToListAsync();

            var now = MV.DomainLayer.Helpers.VietnamTimeHelper.Now;
            foreach (var token in tokens)
            {
                token.Revokedat = now;
            }
        }

        public async Task RevokeAllByUserIdAsync(string userId)
        {
            var tokens = await _context.Refreshtokens
                .Where(t => t.Userid == userId && t.Revokedat == null)
                .ToListAsync();

            var now = MV.DomainLayer.Helpers.VietnamTimeHelper.Now;
            foreach (var token in tokens)
            {
                token.Revokedat = now;
            }
        }
    }
}
