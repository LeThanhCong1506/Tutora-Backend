using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces;

public interface ILearningMaterialRepository
{
    Task<List<Learningmaterial>> GetByBookingIdAsync(int bookingId);
    Task<Learningmaterial?> GetByIdAsync(int materialId);
    void Add(Learningmaterial material);
    void Remove(Learningmaterial material);
    Task<int> SaveChangesAsync();
}
