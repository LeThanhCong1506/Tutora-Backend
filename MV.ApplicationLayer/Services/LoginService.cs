using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel;
using Google.Apis.Auth;
using MV.DomainLayer.Configuration;

namespace MV.ApplicationLayer.Services
{
    public class LoginService : ILoginService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<LoginService> _logger;
        private readonly ISocialRegistrationService _socialRegistrationService;

        public LoginService(
            IConfiguration configuration,
            ILogger<LoginService> logger,
            ISocialRegistrationService socialRegistrationService)
        {
            _configuration = configuration;
            _logger = logger;
            _socialRegistrationService = socialRegistrationService;
        }

        public async Task<TokenResponse> AuthenticateGoogleUserAsync(string idToken)
        {
            try
            {
                // 1. Validate Google ID Token
                var (payload, validationError) = await ValidateGoogleTokenAsync(idToken);
                if (payload == null)
                {
                    return new TokenResponse
                    {
                        ErrorMessage = validationError ?? "Invalid Google ID token."
                    };
                }

                // 2. Extract Email
                var email = payload.Email;
                if (string.IsNullOrEmpty(email))
                {
                    return new TokenResponse { ErrorMessage = "Invalid Token: Email is missing from Google account." };
                }

                return await _socialRegistrationService.BeginAsync(
                    SocialAuthProvider.Google,
                    payload.Subject ?? email,
                    email,
                    payload.Name,
                    payload.Picture);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Google authentication.");
                return new TokenResponse { ErrorMessage = "Đã xảy ra lỗi trong quá trình xác thực bằng Google." };
            }
        }

        #region Helper Methods

        private async Task<(GoogleJsonWebSignature.Payload? Payload, string? ErrorMessage)> ValidateGoogleTokenAsync(string idToken)
        {
            try
            {
                var googleSettings = _configuration.GetSection(GoogleSettings.SectionName).Get<GoogleSettings>();
                var settings = new GoogleJsonWebSignature.ValidationSettings();
                
                if (googleSettings != null && !string.IsNullOrEmpty(googleSettings.ClientId))
                {
                    settings.Audience = new[] { googleSettings.ClientId };
                }

                var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
                return (payload, null);
            }
            catch (InvalidJwtException ex)
            {
                _logger.LogWarning(ex, "Google token validation failed.");
                return (null, "Invalid Google ID token.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while validating Google token.");
                return (null, "Error while validating Google ID token.");
            }
        }

        #endregion
    }
}
