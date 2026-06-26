using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;
using System.Text.Json;

namespace MV.ApplicationLayer.Services
{
    public partial class TutorVerificationService
    {
        // ─── Verification Progress Sections ──────────────────────────────────

        public async Task<VerificationProgressResponse?> GetVerificationProgressAsync(string userId)
        {
            var user = await _unitOfWork.UserRepository.GetUserByIdAsync(userId);
            if (user == null) return null;

            var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(userId);
            var certificates = await _unitOfWork.TutorRepository.GetCertificatesByTutorIdAsync(userId);

            return new VerificationProgressResponse
            {
                Sections = new VerificationSections
                {
                    Video = BuildVideoSection(profile),
                    BasicInfo = BuildBasicInfoSection(profile, user),
                    Introduction = BuildIntroductionSection(profile),
                    Certificates = BuildCertificatesSection(certificates, profile),
                    IdentityCard = BuildIdentityCardSection(user, _encryption)
                }
            };
        }

        // ─── Section builders ─────────────────────────────────────────────────

        private static VideoSection BuildVideoSection(Tutorprofile? profile)
        {
            var hasVideo = !string.IsNullOrWhiteSpace(profile?.Videointrourl);
            return new VideoSection
            {
                Status = hasVideo ? SectionStatus.Updated : SectionStatus.InProgress,
                UpdatedAt = hasVideo && profile?.Updatedat != null ? profile.Updatedat.Value : (DateTime?)null,
                VideoUrl = profile?.Videointrourl
            };
        }

        private static BasicInfoSection BuildBasicInfoSection(Tutorprofile? profile, User user)
        {
            var isComplete = !string.IsNullOrWhiteSpace(profile?.Headline);

            return new BasicInfoSection
            {
                Status = isComplete ? SectionStatus.Updated : SectionStatus.InProgress,
                UpdatedAt = isComplete && profile?.Updatedat != null ? profile.Updatedat.Value : (DateTime?)null,
                AvatarUrl = user.Avatarurl,
                Headline = profile?.Headline,
                TeachingAreaCity = profile?.Teachingareacity,
                TeachingAreaDistrict = profile?.Teachingareadistrict,
                TeachingMode = TeachingMode.Online
            };
        }

        private static IntroductionSection BuildIntroductionSection(Tutorprofile? profile)
        {
            var hasBio = !string.IsNullOrWhiteSpace(profile?.Bio);
            var hasEducation = !string.IsNullOrWhiteSpace(profile?.Education);
            var isComplete = hasBio && hasEducation;

            return new IntroductionSection
            {
                Status = isComplete ? SectionStatus.Updated : SectionStatus.InProgress,
                UpdatedAt = isComplete && profile?.Updatedat != null ? profile.Updatedat.Value : (DateTime?)null,
                Bio = profile?.Bio,
                Education = profile?.Education,
                Gpa = profile?.Gpa,
                GpaScale = profile?.Gpascale,
                Experience = profile?.Experience
            };
        }

        private static CertificatesSection BuildCertificatesSection(List<Tutorcertificate>? certificates, Tutorprofile? profile)
        {
            var hasCertificates = certificates != null && certificates.Count > 0;
            var maxCertDate = hasCertificates ? certificates?.Max(c => c.Updatedat ?? c.Createdat) : null;

            return new CertificatesSection
            {
                Status = hasCertificates ? SectionStatus.Updated : SectionStatus.InProgress,
                UpdatedAt = maxCertDate,
                TotalCount = certificates?.Count ?? 0,
                Certificates = certificates?.Select(c => new CertificateResponse
                {
                    CertificateId = c.Certificateid,
                    CertificateName = c.Certificatename,
                    CertificateType = c.Certificatetype,
                    IssuingOrganization = c.Issuingorganization,
                    YearIssued = c.Yearissued,
                    CredentialId = c.Credentialid,
                    CredentialUrl = c.Credentialurl,
                    CertificateFileUrl = c.Certificatefileurl,
                    CreatedAt = c.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
                    VerificationStatus = c.Verificationstatus,
                    VerificationNote = c.Verificationnote
                }).ToList()
            };
        }

        private static IdentityCardSection BuildIdentityCardSection(User user, IEncryptionService encryption)
        {
            var isVerified = user.Isidentityverified ?? false;
            var hasIdentity = !string.IsNullOrWhiteSpace(user.Identitynumber);
            var isComplete = hasIdentity && isVerified;

            // Parse Ekycrawdata để lấy thông tin CCCD
            string? fullName = null;
            string? dateOfBirth = null;
            string? gender = null;
            string? permanentAddress = null;

            var rawEkyc = encryption.Decrypt(user.Ekycrawdata);
            if (!string.IsNullOrWhiteSpace(rawEkyc))
            {
                try
                {
                    using var doc = JsonDocument.Parse(rawEkyc);
                    if (doc.RootElement.TryGetProperty("OcrResult", out var ocr))
                    {
                        fullName = ocr.TryGetProperty("name", out var n) ? n.GetString() : null;
                        dateOfBirth = ocr.TryGetProperty("dob", out var d) ? d.GetString() : null;
                        permanentAddress = ocr.TryGetProperty("address", out var a) ? a.GetString() : null;
                        var rawSex = ocr.TryGetProperty("sex", out var s) ? s.GetString() : null;
                        gender = NormalizeSex(rawSex);
                    }
                }
                catch { /* bỏ qua nếu parse lỗi */ }
            }

            // Fallback sang User entity nếu Ekycrawdata chưa có
            fullName ??= user.Fullname;
            permanentAddress ??= user.Address;
            if (gender == null && user.Gender != null)
                gender = user.Gender.ToString();
            if (dateOfBirth == null && user.Birthdate.HasValue)
                dateOfBirth = user.Birthdate.Value.ToString("dd/MM/yyyy");

            return new IdentityCardSection
            {
                Status = isComplete ? SectionStatus.Updated : SectionStatus.InProgress,
                UpdatedAt = isComplete ? user.Createdat : null,
                IdentityNumberMasked = MaskIdentityNumber(encryption.Decrypt(user.Identitynumber)),
                FullName = fullName,
                DateOfBirth = dateOfBirth,
                Gender = gender,
                PermanentAddress = permanentAddress,
                PortraitImageUrl = user.Avatarurl,
                IsVerified = isVerified
            };
        }

        private static string? MaskIdentityNumber(string? number)
        {
            if (string.IsNullOrWhiteSpace(number) || number.Length < 6) return null;
            // Hiển thị 3 số đầu và 4 số cuối, che giữa
            var visible = Math.Min(3, number.Length);
            var tail = Math.Min(4, number.Length - visible);
            var maskLen = number.Length - visible - tail;
            return number[..visible] + new string('*', maskLen) + number[^tail..];
        }

        private static string? NormalizeSex(string? sex)
        {
            if (string.IsNullOrWhiteSpace(sex)) return null;
            return sex.Trim().ToLower() switch
            {
                "nam" or "male" or "m" => "Nam",
                "nữ" or "nu" or "female" or "f" => "Nữ",
                _ => sex
            };
        }
    }
}
