using MV.DomainLayer.DTO.ResponseModel;
using System.Security.Claims;

namespace MV.ApplicationLayer.RepositoryInterfaces
{
    public interface IAuthenticationRepository
    {
        string GenerateJwtToken(LoginResponse loginResponse);
        string GenerateRefreshToken();
        string HashToken(string token);
        ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
    }
}
