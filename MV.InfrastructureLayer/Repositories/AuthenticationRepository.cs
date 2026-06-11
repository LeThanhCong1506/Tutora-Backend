using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel;
using MV.ApplicationLayer.RepositoryInterfaces;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace MV.InfrastructureLayer.Repositories
{
    public class AuthenticationRepository : IAuthenticationRepository
    {
        private readonly IConfiguration _configuration;
        private readonly string? _jwtKey;
        private readonly string? _jwtIssuer;
        private readonly string? _jwtAudience;

        public AuthenticationRepository(IConfiguration configuration)
        {
            _configuration = configuration;
            _jwtKey = _configuration[ConfigurationKeys.Jwt.Key];
            _jwtIssuer = _configuration[ConfigurationKeys.Jwt.Issuer];
            _jwtAudience = _configuration[ConfigurationKeys.Jwt.Audience];

            if (string.IsNullOrEmpty(_jwtKey) || string.IsNullOrEmpty(_jwtIssuer))
            {
                throw new InvalidOperationException("JWT configuration is missing in appsettings.json for AuthService.");
            }
        }

        public string GenerateJwtToken(LoginResponse loginResponse)
        {
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtKey!));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, loginResponse.Userid!),
                new Claim(JwtRegisteredClaimNames.Sub, loginResponse.Username!),
                new Claim(JwtRegisteredClaimNames.Email, loginResponse.Email!),
                new Claim(ClaimTypes.Name, loginResponse.Fullname ?? string.Empty),
                new Claim(ClaimTypes.MobilePhone, loginResponse.Phone!),
                new Claim(ClaimTypes.Role, loginResponse.Role!),
                new Claim(ApplicationClaimTypes.UserId, loginResponse.Userid!)
            };

            var token = new JwtSecurityToken(
                issuer: _jwtIssuer,
                audience: _jwtAudience,
                claims: claims,
                expires: MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddHours(1),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public string GenerateRefreshToken()
        {
            var randomBytes = RandomNumberGenerator.GetBytes(64);
            return Convert.ToBase64String(randomBytes);
        }

        public string HashToken(string token)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(hash).ToLower();
        }

        public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
        {
            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtKey!)),
                ValidateIssuer = true,
                ValidIssuer = _jwtIssuer,
                ValidateAudience = true,
                ValidAudience = _jwtAudience,
                ValidateLifetime = false, // bỏ qua check hết hạn
                ClockSkew = TimeSpan.Zero
            };

            try
            {
                var principal = new JwtSecurityTokenHandler()
                    .ValidateToken(token, tokenValidationParameters, out var securityToken);

                if (securityToken is not JwtSecurityToken jwtSecurityToken ||
                    !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
                {
                    return null;
                }

                return principal;
            }
            catch
            {
                return null;
            }
        }
    }
}
