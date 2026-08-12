using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using MV.DomainLayer.Configuration;
using MV.DomainLayer.Constants;
using MV.InfrastructureLayer.Services;
using System.Security.Claims;
using System.Security.Cryptography;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Đọc file private lưu trên đĩa VPS (CCCD, ảnh proof payout...).
///
/// Ba lớp bảo vệ, phải qua HẾT mới đọc được file:
///   1. Phải đăng nhập (JWT) — link lọt ra ngoài cho người chưa đăng nhập là vô dụng.
///   2. Phải ĐÚNG NGƯỜI: chủ sở hữu file, hoặc Admin/Staff có quyền xem CCCD.
///   3. Chữ ký HMAC + hạn dùng trong query — link hết hạn hoặc bị sửa đều bị từ chối.
///
/// Vì thẻ &lt;img&gt; không gửi được header Authorization, phía giao diện phải tải ảnh bằng
/// JavaScript (fetch kèm token) rồi đổi sang blob URL để hiển thị.
/// </summary>
[ApiController]
[Route("api/files")]
[Authorize]
public class PrivateFileController(IOptions<LocalStorageSettings> settings, ILogger<PrivateFileController> logger) : ControllerBase
{
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    /// <summary>Đường dẫn luôn có dạng "{bucket}/{userId}/{tên file}" (xem LocalFileStorageService.BuildRelativePath),
    /// nên đoạn thứ hai chính là chủ sở hữu file. Với ảnh proof payout, đoạn này là "withdrawal-{id}" —
    /// không trùng userId của ai cả, nên chỉ Admin/Staff có quyền mới xem được, đúng như mong muốn.</summary>
    private static string? ExtractOwnerId(string relativePath)
    {
        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2 ? segments[1] : null;
    }

    private bool CanAccess(string relativePath)
    {
        if (User.IsInRole(UserRole.Admin)) return true;

        if (User.IsInRole(UserRole.Staff)
            && User.HasClaim(Permissions.ClaimType, Permissions.TutorCccdView)) return true;

        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return !string.IsNullOrEmpty(callerId)
            && string.Equals(ExtractOwnerId(relativePath), callerId, StringComparison.Ordinal);
    }

    [HttpGet("private")]
    public IActionResult GetPrivateFile([FromQuery] string path, [FromQuery] long expires, [FromQuery] string sig)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(sig))
            return BadRequest();

        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expires)
            return StatusCode(StatusCodes.Status410Gone, "Link xem file đã hết hạn.");

        byte[] providedSignature;
        try
        {
            providedSignature = Convert.FromHexString(sig);
        }
        catch (FormatException)
        {
            return BadRequest();
        }

        var expectedSignature = LocalFileStorageService.ComputeSignature(path, expires, settings.Value.SigningKey);
        if (!CryptographicOperations.FixedTimeEquals(providedSignature, expectedSignature))
            return Forbid();

        // Chữ ký hợp lệ vẫn chưa đủ: link có thể bị chuyển cho người khác. Bắt buộc đúng chủ sở hữu
        // hoặc Admin/Staff có quyền — đây là lớp chặn "người lạ cầm được link".
        if (!CanAccess(path))
        {
            logger.LogWarning("Từ chối truy cập file private không thuộc quyền: {Path}", path);
            return Forbid();
        }

        var root = settings.Value.PrivateRoot;
        var rootFullPath = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        // Chặn path traversal: dù chữ ký hợp lệ, kết quả PHẢI nằm trong đúng PrivateRoot.
        if (!fullPath.StartsWith(rootFullPath, StringComparison.Ordinal))
        {
            logger.LogWarning("Từ chối path ngoài PrivateRoot: {Path}", path);
            return BadRequest();
        }

        if (!System.IO.File.Exists(fullPath))
            return NotFound();

        if (!ContentTypeProvider.TryGetContentType(fullPath, out var contentType))
            contentType = "application/octet-stream";

        return PhysicalFile(fullPath, contentType);
    }
}
