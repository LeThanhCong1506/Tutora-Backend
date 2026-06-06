using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.Attributes;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public class ImageFileAttribute : ValidationAttribute
{
    private const int MaxSizeMB = 5;
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private static readonly string[] AllowedMimeTypes = ["image/jpeg", "image/png", "image/webp"];

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value == null) return ValidationResult.Success;

        if (value is not IFormFile file)
            return new ValidationResult("Invalid file type.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
            return new ValidationResult($"Only {string.Join(", ", AllowedExtensions)} files are allowed.");

        if (!AllowedMimeTypes.Contains(file.ContentType))
            return new ValidationResult($"Only image files are allowed.");

        if (file.Length > MaxSizeMB * 1024 * 1024)
            return new ValidationResult($"File size must be less than {MaxSizeMB}MB.");

        return ValidationResult.Success;
    }
}
