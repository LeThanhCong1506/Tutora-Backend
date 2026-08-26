using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Services
{
    public partial class TutorService
    {

        public async Task<CertificateUploadResponse> AddCertificateAsync(string tutorId, AddCertificateRequest request)
        {
            var profile = await _tutorRepository.GetTutorProfileByIdAsync(tutorId)
                ?? throw new ArgumentException("Không tìm thấy hồ sơ gia sư.");

            ValidateCertificateFile(request.CertificateFile); // Kiểm tra định dạng và kích thước file

            if (request.YearIssued.HasValue)
            {
                var currentYear = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.Year;
                if (request.YearIssued < 1900 || request.YearIssued > currentYear)
                    throw new ArgumentException($"Năm cấp phải nằm trong khoảng 1900 đến {currentYear}.");
            }

            var certificateFileUrl = await _storageService.UploadFileAsync(
                CertificateBucket, tutorId, request.CertificateFile);
            // Nếu file là PDF, tạo thumbnail (ảnh JPG) từ trang đầu tiên của PDF
            var thumbnailUrl = await TryGeneratePdfThumbnailAsync(request.CertificateFile, tutorId);

            var certificate = new Tutorcertificate
            {
                Certificateid = Guid.NewGuid().ToString(),
                Tutorid = tutorId,
                Certificatename = request.CertificateName,
                Certificatetype = request.CertificateType,
                Issuingorganization = request.IssuingOrganization,
                Yearissued = request.YearIssued,
                Credentialid = request.CredentialId,
                Credentialurl = request.CredentialUrl,
                Certificatefileurl = certificateFileUrl,
                Thumbnailurl = thumbnailUrl,
                Verificationstatus = CertificateStatus.PendingReview,
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
                Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            };

            await _tutorRepository.AddCertificateAsync(certificate);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Certificate {CertId} uploaded for tutor {TutorId}, awaiting admin review",
                certificate.Certificateid, tutorId);

            return new CertificateUploadResponse
            {
                Certificate = MapToCertificateResponse(certificate)
            };
        }

        public async Task<List<CertificateResponse>> GetCertificatesAsync(string tutorId)
        {
            var certificates = await _tutorRepository.GetCertificatesByTutorIdAsync(tutorId);
            return certificates.Select(MapToCertificateResponse).ToList();
        }

        public async Task<CertificateResponse?> UpdateCertificateAsync(string tutorId, string certificateId, UpdateCertificateRequest request)
        {
            var certificate = await _tutorRepository.GetCertificateByIdAsync(certificateId);
            if (certificate == null) return null;

            if (certificate.Tutorid != tutorId)
            {
                throw new UnauthorizedAccessException("Bạn chỉ có thể cập nhật chứng chỉ của mình.");
            }

            if (!string.IsNullOrWhiteSpace(request.CertificateName))
                certificate.Certificatename = request.CertificateName;
            if (!string.IsNullOrWhiteSpace(request.CertificateType))
                certificate.Certificatetype = request.CertificateType;
            if (!string.IsNullOrWhiteSpace(request.IssuingOrganization))
                certificate.Issuingorganization = request.IssuingOrganization;
            if (request.YearIssued.HasValue)
                certificate.Yearissued = request.YearIssued;
            if (!string.IsNullOrWhiteSpace(request.CredentialId))
                certificate.Credentialid = request.CredentialId;
            if (!string.IsNullOrWhiteSpace(request.CredentialUrl))
                certificate.Credentialurl = request.CredentialUrl;

            // Upload new file if provided
            if (request.CertificateFile != null && request.CertificateFile.Length > 0)
            {
                ValidateCertificateFile(request.CertificateFile);
                var newFileUrl = await _storageService.UploadFileAsync(CertificateBucket, tutorId, request.CertificateFile);
                certificate.Certificatefileurl = newFileUrl;
                // File mới thay thế hoàn toàn file cũ — thumbnail cũ (nếu có) không còn khớp nữa,
                // luôn tính lại (null nếu file mới không phải PDF, đúng ý nghĩa "không có thumbnail").
                certificate.Thumbnailurl = await TryGeneratePdfThumbnailAsync(request.CertificateFile, tutorId);
            }

            certificate.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            await _context.SaveChangesAsync();

            return MapToCertificateResponse(certificate);
        }

        public async Task<bool> DeleteCertificateAsync(string tutorId, string certificateId)
        {
            var certificate = await _tutorRepository.GetCertificateByIdAsync(certificateId);
            if (certificate == null) return false;

            if (certificate.Tutorid != tutorId)
            {
                throw new UnauthorizedAccessException("Bạn chỉ có thể xóa chứng chỉ của mình.");
            }

            _tutorRepository.DeleteCertificate(certificate);
            await _context.SaveChangesAsync();
            return true;
        }

        // ─── Private helpers ─────────────────────────────────────────────────

        private void ValidateCertificateFile(IFormFile file)
        {
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!AllowedCertificateExtensions.Contains(extension))
            {
                throw new ArgumentException($"Only {string.Join(", ", AllowedCertificateExtensions)} files are allowed for certificates");
            }

            if (file.Length > MaxCertificateFileSize)
            {
                throw new ArgumentException($"Certificate file must be less than {MaxCertificateFileSize / (1024 * 1024)}MB");
            }
        }

        /// <summary>Render trang 1 của chứng chỉ PDF thành ảnh JPG (thay cho Cloudinary transformation cũ,
        /// giờ tự lưu file nên phải tự làm). Ảnh thường (jpg/png) thì bỏ qua, trả null luôn.
        /// Lỗi render (PDF hỏng...) không được làm hỏng cả việc upload chứng chỉ — chỉ log cảnh báo,
        /// FE đã có fallback icon khi không có thumbnail.</summary>
        private async Task<string?> TryGeneratePdfThumbnailAsync(IFormFile file, string tutorId)
        {
            if (!string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
                return null;

            try
            {
                byte[] pdfBytes;
                using (var stream = file.OpenReadStream())
                using (var ms = new MemoryStream())
                {
                    await stream.CopyToAsync(ms);
                    pdfBytes = ms.ToArray();
                }

                using var bitmap = PDFtoImage.Conversion.ToImage(pdfBytes, page: 0);
                using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
                using var jpegData = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 85);

                var thumbFileName = $"{Path.GetFileNameWithoutExtension(file.FileName)}_thumb.jpg";
                return await _storageService.UploadImageBytesAsync(CertificateBucket, tutorId, jpegData.ToArray(), thumbFileName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không tạo được thumbnail cho chứng chỉ PDF của tutor {TutorId}", tutorId);
                return null;
            }
        }

        private static CertificateResponse MapToCertificateResponse(Tutorcertificate certificate)
        {
            return new CertificateResponse
            {
                CertificateId = certificate.Certificateid,
                CertificateName = certificate.Certificatename,
                CertificateType = certificate.Certificatetype,
                IssuingOrganization = certificate.Issuingorganization,
                YearIssued = certificate.Yearissued,
                CredentialId = certificate.Credentialid,
                CredentialUrl = certificate.Credentialurl,
                CertificateFileUrl = certificate.Certificatefileurl,
                ThumbnailUrl = certificate.Thumbnailurl,
                CreatedAt = certificate.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
                VerificationStatus = certificate.Verificationstatus,
                VerificationNote = certificate.Verificationnote
            };
        }

    }
}
