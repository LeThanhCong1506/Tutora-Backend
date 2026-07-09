using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;

namespace MV.InfrastructureLayer.Repositories;

public class LearningMaterialRepository(AgoraDbContext context) : ILearningMaterialRepository
{
    public Task<List<Learningmaterial>> GetByBookingIdAsync(int bookingId)
        => context.Learningmaterials
            .AsNoTracking()
            .Where(m => m.Bookingid == bookingId)
            .OrderByDescending(m => m.Createdat)
            .ToListAsync();

    public Task<Learningmaterial?> GetByIdAsync(int materialId)
        => context.Learningmaterials.FirstOrDefaultAsync(m => m.Materialid == materialId);

    public void Add(Learningmaterial material)
        => context.Learningmaterials.Add(material);

    public void Remove(Learningmaterial material)
        => context.Learningmaterials.Remove(material);

    public Task<int> SaveChangesAsync()
        => context.SaveChangesAsync();
}
