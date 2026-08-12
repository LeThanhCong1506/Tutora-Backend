using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using MV.DomainLayer.Configuration;
using MV.InfrastructureLayer.Services;
using System.Security.Cryptography;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Đọc file private lưu trên đĩa VPS (CCCD, ảnh proof payout...) — thay cho signed URL của Cloudinary.
/// Không yêu cầu JWT: chữ ký HMAC + hạn dùng trong query string chính là bằng chứng đã được backend
/// xác thực quyền TRƯỚC KHI tạo link (xem IFileStorageService.GenerateSignedUrl) — ai không có link hợp lệ
/// (đúng chữ ký, chưa hết hạn) thì không đọc được, y hệt cơ chế signed URL của Cloudinary.
/// </summary>
[ApiController]
[Route("api/files")]
[AllowAnonymous]
public class PrivateFileController(IOptions<LocalStorageSettings> settings, ILogger<PrivateFileController> logger) : ControllerBase
{
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

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
