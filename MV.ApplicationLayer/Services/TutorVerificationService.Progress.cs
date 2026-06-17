using Microsoft.Extensions.Logging;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;

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
            var subjects = await _unitOfWork.TutorRepository.GetTutorSubjectsByTutorIdAsync(userId);
            var prices = await _unitOfWork.TutorRepository.GetTutorSubjectGradePricesAsync(userId);
            var certificates = await _unitOfWork.TutorRepository.GetCertificatesByTutorIdAsync(userId);

            return new VerificationProgressResponse
            {
                Sections = new VerificationSections
                {
                    Video = BuildVideoSection(profile),
                    BasicInfo = BuildBasicInfoSection(profile, user),
                    Introduction = BuildIntroductionSection(profile),
                    Certificates = BuildCertificatesSection(certificates, profile),
                    IdentityCard = await BuildIdentityCardSectionAsync(user),
                    Pricing = BuildPricingSection(profile, prices)
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

        private Task<IdentityCardSection> BuildIdentityCardSectionAsync(User user)
        {
            var hasFront = !string.IsNullOrWhiteSpace(user.Idcardfronturl);
            var hasBack = !string.IsNullOrWhiteSpace(user.Idcardbackurl);
            var isComplete = hasFront && hasBack;

            return Task.FromResult(new IdentityCardSection
            {
                Status = isComplete ? SectionStatus.Updated : SectionStatus.InProgress,
                UpdatedAt = isComplete ? user.Createdat : null,
                FrontImageUrl = user.Idcardfronturl,
                BackImageUrl = user.Idcardbackurl,
                IsVerified = user.Isidentityverified ?? false
            });
        }

        private static PricingSection BuildPricingSection(Tutorprofile? profile, List<Tutorsubjectgradeprice>? prices)
        {
            var hourlyRate = prices?
                .Where(p => p.Isactive)
                .OrderBy(p => p.Priceperhour)
                .Select(p => (decimal?)p.Priceperhour)
                .FirstOrDefault();
            var hasHourlyRate = hourlyRate.HasValue && hourlyRate.Value > 0;

            return new PricingSection
            {
                Status = hasHourlyRate ? SectionStatus.Updated : SectionStatus.InProgress,
                UpdatedAt = hasHourlyRate && profile?.Updatedat != null ? profile.Updatedat.Value : (DateTime?)null
            };
        }
    }
}
