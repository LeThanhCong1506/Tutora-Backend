using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;

namespace MV.PresentationLayer.Controllers;

[Route("api/auth/social")]
[ApiController]
public class SocialRegistrationController : ControllerBase
{
    private readonly ISocialRegistrationService _socialRegistrationService;

    public SocialRegistrationController(ISocialRegistrationService socialRegistrationService)
    {
        _socialRegistrationService = socialRegistrationService;
    }

    /// <summary>
    /// Nhận role + số điện thoại sau khi Google/Zalo đã được xác thực và gửi OTP.
    /// </summary>
    [HttpPost("complete-registration")]
    [EnableRateLimiting("otp")]
    public async Task<IActionResult> CompleteRegistration(
        [FromBody] CompleteSocialRegistrationRequest request)
    {
        var result = await _socialRegistrationService.CompleteRegistrationAsync(request);
        if (!string.IsNullOrEmpty(result.ErrorMessage))
            return BadRequest(new APIResponse<object>(400, result.ErrorMessage, BuildPendingData(result)));

        return Ok(new APIResponse<object>(
            200,
            "Mã OTP đã được gửi tới số điện thoại.",
            BuildPendingData(result)));
    }

    /// <summary>
    /// Xác thực OTP, tạo hoặc cập nhật tài khoản social và cấp JWT.
    /// </summary>
    [HttpPost("verify-phone")]
    [EnableRateLimiting("otp")]
    public async Task<IActionResult> VerifyPhone([FromBody] VerifySocialPhoneOtpRequest request)
    {
        var result = await _socialRegistrationService.VerifyPhoneOtpAsync(request);
        if (!string.IsNullOrEmpty(result.ErrorMessage))
            return BadRequest(new APIResponse<object>(400, result.ErrorMessage, BuildPendingData(result)));

        return Ok(new APIResponse<object>(200, "Xác thực số điện thoại thành công.", new
        {
            token = result.AccessToken,
            refreshToken = result.RefreshToken
        }));
    }

    /// <summary>
    /// Gửi lại OTP cho phiên đăng ký social hiện tại.
    /// </summary>
    [HttpPost("resend-phone-otp")]
    [EnableRateLimiting("otp")]
    public async Task<IActionResult> ResendPhoneOtp(
        [FromBody] ResendSocialPhoneOtpRequest request)
    {
        var result = await _socialRegistrationService.ResendPhoneOtpAsync(request);
        if (!string.IsNullOrEmpty(result.ErrorMessage))
            return BadRequest(new APIResponse<object>(400, result.ErrorMessage, BuildPendingData(result)));

        return Ok(new APIResponse<object>(
            200,
            "Đã gửi lại mã OTP.",
            BuildPendingData(result)));
    }

    private static object BuildPendingData(MV.DomainLayer.DTO.ResponseModel.TokenResponse result)
        => new
        {
            requiresRoleSelection = result.RequiresRoleSelection,
            requiresPhoneInput = result.RequiresPhoneInput,
            requiresPhoneVerification = result.RequiresPhoneVerification,
            socialRegistrationToken = result.SocialRegistrationToken,
            phone = result.Phone
        };
}
