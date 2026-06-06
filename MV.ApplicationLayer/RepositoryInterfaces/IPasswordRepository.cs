namespace MV.ApplicationLayer.RepositoryInterfaces
{
    public interface IPasswordRepository
    {
        string HashPassword(string password);
        bool VerifyPassword(string password, string storedHash);
    }
}
