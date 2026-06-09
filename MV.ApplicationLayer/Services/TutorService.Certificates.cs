using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services
{
    public partial class TutorService
    {
        // ─── Certificate Methods ─────────────────────────────────────────────

        public async Task<CertificateUploadResponse> AddCertificateAsync(string tutorId, AddCertificateRequest request)
        {
            // Validate tutor exists
            var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(tutorId);
            if (profile == null)
            {
                throw new ArgumentException("Không tìm thấy hồ sơ gia sư");
            }

            // Get user info for name comparison
            var user = await _unitOfWork.UserRepository.GetUserByIdAsync(tutorId);
            if (user == null)
            {
                throw new ArgumentException("Không tìm thấy người dùng");
            }

            // Validate certificate file
            ValidateCertificateFile(request.CertificateFile);

            // Validate year issued (if provided)
            if (request.YearIssued.HasValue)
            {
                var currentYear = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.Year;
                if (request.YearIssued < 1900 || request.YearIssued > currentYear)
                {
                    throw new ArgumentException($"Năm cấp phải nằm trong khoảng 1900 đến {currentYear}");
                }
            }

            // Upload certificate file to Storage
            var certificateFileUrl = await _storageService.UploadFileAsync(
                CertificateBucket,
                tutorId,
                request.CertificateFile);

            // Create certificate entity
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
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
                Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            };

            // Save certificate to DB first
            await _unitOfWork.TutorRepository.AddCertificateAsync(certificate);
            await _unitOfWork.SaveChangesAsync();

            // Auto-check certificate (chỉ trả về result, KHÔNG lưu vào DB)
            var validationSummary = new CertificateValidationSummary
            {
                IsValid = true,
                Errors = new List<string>(),
                Checks = new ValidationCheckResults()
            };

            try
            {
                using var certificateStream = request.CertificateFile.OpenReadStream();
                var fileExtension = Path.GetExtension(request.CertificateFile.FileName).ToLowerInvariant();

                Stream streamForOcr = certificateStream;
                string fileNameForOcr = request.CertificateFile.FileName;
                MemoryStream? convertedImageStream = null;

                // Nếu là PDF, convert sang image trước
                if (fileExtension == ".pdf")
                {
                    convertedImageStream = await ConvertPdfToImageAsync(certificateStream);
                    streamForOcr = convertedImageStream;
                    fileNameForOcr = Path.ChangeExtension(request.CertificateFile.FileName, ".png");
                }

                try
                {
                    var validationResult = await _certificateVerificationService.AutoValidateCertificateAsync(
                        certificate,
                        user.Fullname ?? "",
                        streamForOcr,
                        fileNameForOcr);

                    // Map to summary
                    validationSummary.IsValid = validationResult.IsAutoVerified;
                    validationSummary.Errors = validationResult.Details?.Errors ?? new List<string>();
                    validationSummary.Checks = new ValidationCheckResults
                    {
                        NameMatched = validationResult.Details?.NameMatched ?? false,
                        NameSimilarity = validationResult.Details?.NameSimilarity ?? 0,
                        IssuerValidByAi = validationResult.Details?.IssuerValidByAi ?? false,
                        IssuerMatchesUserInput = validationResult.Details?.IssuerMatchesUserInput ?? false,
                        IssuerValidationPassed = validationResult.Details?.IssuerValidationPassed ?? false,
                        ExtractedDateIsValid = validationResult.Details?.ExtractedDateIsValid ?? false,
                        YearMatchesUserInput = validationResult.Details?.YearMatchesUserInput ?? false,
                        UserInputYearIsValid = validationResult.Details?.UserInputYearIsValid ?? false,
                        DateValidationPassed = validationResult.Details?.DateValidationPassed ?? false,
                        OcrSuccess = validationResult.Details?.OcrSuccess ?? false,
                        OcrConfidence = validationResult.Details?.OcrConfidence ?? 0
                    };

                    _logger.LogInformation(
                        "Certificate {CertificateId} auto-check result: IsValid={IsValid}",
                        certificate.Certificateid, validationSummary.IsValid);
                }
                finally
                {
                    convertedImageStream?.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auto-check failed for certificate, treating as invalid");
                validationSummary.IsValid = false;
                validationSummary.Errors.Add($"Auto-check error: {ex.Message}");
            }

            // Lưu verification status vào certificate
            certificate.Verificationstatus = validationSummary.IsValid
                ? CertificateStatus.Verified
                : CertificateStatus.PendingReview;
            certificate.Verificationnote = validationSummary.IsValid
                ? "Auto-verified by system"
                : string.Join("; ", validationSummary.Errors);
            certificate.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            await _unitOfWork.SaveChangesAsync();

            // Nếu valid → check required fields và set Active
            bool isProfileActivated = false;
            if (validationSummary.IsValid)
            {
                var subjects = await _unitOfWork.TutorRepository.GetTutorSubjectsByTutorIdAsync(tutorId);
                var prices = await _unitOfWork.TutorRepository.GetTutorSubjectGradePricesAsync(tutorId);
                bool hasRequiredFields = CheckRequiredFields(profile, user, subjects, prices);
                bool identityVerified = user.Isidentityverified ?? false;

                if (hasRequiredFields && identityVerified)
                {
                    profile.Profilestatus = TutorProfileStatus.Active;
                    profile.Ispublic = true;
                    profile.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
                    await _unitOfWork.SaveChangesAsync();
                    isProfileActivated = true;

                    _logger.LogInformation("Profile {TutorId} activated after valid certificate upload", tutorId);
                }
            }

            return new CertificateUploadResponse
            {
                Certificate = MapToCertificateResponse(certificate),
                ValidationResult = validationSummary,
                IsProfileActivated = isProfileActivated
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
                CreatedAt = TimeZoneHelper.ToUserTime(certificate.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow),
                VerificationStatus = certificate.Verificationstatus,
                VerificationNote = certificate.Verificationnote
            };
        }

        /// <summary>Convert first page of PDF to PNG stream for OCR.</summary>
        private static async Task<MemoryStream> ConvertPdfToImageAsync(Stream pdfStream)
        {
            var outputStream = new MemoryStream();
            pdfStream.Position = 0;

            var options = new PDFtoImage.RenderOptions(
                Dpi: 300,
                WithAnnotations: true,
                WithFormFill: true
            );

            PDFtoImage.Conversion.SavePng(outputStream, pdfStream, page: 0, options: options);
            outputStream.Position = 0;
            return await Task.FromResult(outputStream);
        }
    }
}
