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
        // ─── Certificate Methods ─────────────────────────────────────────────

        public async Task<CertificateUploadResponse> AddCertificateAsync(string tutorId, AddCertificateRequest request)
        {
            var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(tutorId)
                ?? throw new ArgumentException("Không tìm thấy hồ sơ gia sư");

            ValidateCertificateFile(request.CertificateFile);

            if (request.YearIssued.HasValue)
            {
                var currentYear = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.Year;
                if (request.YearIssued < 1900 || request.YearIssued > currentYear)
                    throw new ArgumentException($"Năm cấp phải nằm trong khoảng 1900 đến {currentYear}");
            }

            var certificateFileUrl = await _storageService.UploadFileAsync(
                CertificateBucket, tutorId, request.CertificateFile);

            var certificate = new Tutorcertificate
            {
                Certificateid       = Guid.NewGuid().ToString(),
                Tutorid             = tutorId,
                Certificatename     = request.CertificateName,
                Certificatetype     = request.CertificateType,
                Issuingorganization = request.IssuingOrganization,
                Yearissued          = request.YearIssued,
                Credentialid        = request.CredentialId,
                Credentialurl       = request.CredentialUrl,
                Certificatefileurl  = certificateFileUrl,
                Verificationstatus  = CertificateStatus.PendingReview,
                Createdat           = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
                Updatedat           = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            };

            await _unitOfWork.TutorRepository.AddCertificateAsync(certificate);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Certificate {CertId} uploaded for tutor {TutorId}, awaiting admin review",
                certificate.Certificateid, tutorId);

            return new CertificateUploadResponse
            {
                Certificate = MapToCertificateResponse(certificate)
            };
        }

        public async Task<List<CertificateResponse>> GetCertificatesAsync(string tutorId)
        {
            var certificates = await _unitOfWork.TutorRepository.GetCertificatesByTutorIdAsync(tutorId);
            return certificates.Select(MapToCertificateResponse).ToList();
        }

        public async Task<CertificateResponse?> UpdateCertificateAsync(string tutorId, string certificateId, UpdateCertificateRequest request)
        {
            var certificate = await _unitOfWork.TutorRepository.GetCertificateByIdAsync(certificateId);
            if (certificate == null) return null;

            if (certificate.Tutorid != tutorId)
            {
                throw new UnauthorizedAccessException("Bạn chỉ có thể cập nhật chứng chỉ của mình");
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
            }

            certificate.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            await _unitOfWork.SaveChangesAsync();

            return MapToCertificateResponse(certificate);
        }

        public async Task<bool> DeleteCertificateAsync(string tutorId, string certificateId)
        {
            var certificate = await _unitOfWork.TutorRepository.GetCertificateByIdAsync(certificateId);
            if (certificate == null) return false;

            if (certificate.Tutorid != tutorId)
            {
                throw new UnauthorizedAccessException("Bạn chỉ có thể xóa chứng chỉ của mình");
            }

            _unitOfWork.TutorRepository.DeleteCertificate(certificate);
            await _unitOfWork.SaveChangesAsync();
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
                CreatedAt = certificate.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
                VerificationStatus = certificate.Verificationstatus,
                VerificationNote = certificate.Verificationnote
            };
        }

    }
}
