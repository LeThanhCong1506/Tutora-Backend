using Microsoft.Extensions.Logging;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services
{
    public partial class TutorService
    {
        // ─── Admin: Certificate Verification ────────────────────────────────────

        public async Task<AdminVerifyCertificateResponse> AdminVerifyCertificateAsync(
            string tutorId, string certId, AdminVerifyCertificateRequest request, string adminId)
        {
            // 1. Lấy certificate, kiểm tra tồn tại và thuộc đúng tutor
            var certificate = await _unitOfWork.TutorRepository.GetCertificateByIdAsync(certId)
                ?? throw new KeyNotFoundException($"Không tìm thấy chứng chỉ với ID: {certId}");

            if (!string.Equals(certificate.Tutorid, tutorId, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Chứng chỉ này không thuộc về gia sư được chỉ định.");

            // 2. Cập nhật trạng thái certificate
            var noteText = request.IsApproved
                ? (!string.IsNullOrWhiteSpace(request.Note) ? request.Note : ApprovalStatusText.NoteApprovedByAdmin)
                : (!string.IsNullOrWhiteSpace(request.Note) ? request.Note : "Chứng chỉ không đạt yêu cầu.");

            certificate.Verificationstatus = request.IsApproved
                ? CertificateStatus.Verified
                : CertificateStatus.Rejected;
            certificate.Verificationnote = $"[Admin: {adminId}] {noteText}";
            certificate.Updatedat = TimeZoneHelper.UtcNow;

            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation(
                "Admin {AdminId} {Action} certificate {CertId} for tutor {TutorId}",
                adminId, request.IsApproved ? "approved" : "rejected", certId, tutorId);

            // 3. Nếu duyệt → re-evaluate profile status
            bool isProfileActivated = false;
            if (request.IsApproved)
            {
                isProfileActivated = await TryActivateProfileAsync(tutorId);
            }

            // 4. Gửi notification cho gia sư
            try
            {
                if (request.IsApproved)
                {
                    await _notificationService.CreateNotificationAsync(new NotificationRequest
                    {
                        Userid = tutorId,
                        Title = "Chứng chỉ được duyệt",
                        Message = isProfileActivated
                            ? $"Chứng chỉ \"{certificate.Certificatename}\" đã được admin phê duyệt. Hồ sơ của bạn đã được kích hoạt!"
                            : $"Chứng chỉ \"{certificate.Certificatename}\" đã được admin phê duyệt."
                    });
                }
                else
                {
                    await _notificationService.CreateNotificationAsync(new NotificationRequest
                    {
                        Userid = tutorId,
                        Title = "Chứng chỉ bị từ chối",
                        Message = $"Chứng chỉ \"{certificate.Certificatename}\" đã bị từ chối. Lý do: {noteText}"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send certificate verification notification to tutor {TutorId}", tutorId);
            }

            return new AdminVerifyCertificateResponse
            {
                CertificateId      = certificate.Certificateid,
                TutorId            = certificate.Tutorid,
                CertificateName    = certificate.Certificatename,
                VerificationStatus = certificate.Verificationstatus!,
                VerificationNote   = certificate.Verificationnote,
                IsProfileActivated = isProfileActivated
            };
        }

        // ─── Private helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Kiểm tra đủ điều kiện và kích hoạt profile Active nếu đủ.
        /// Trả về true nếu profile được kích hoạt lần này.
        /// </summary>
        private async Task<bool> TryActivateProfileAsync(string tutorId)
        {
            var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(tutorId);
            if (profile == null) return false;

            // Chỉ xem xét kích hoạt nếu profile đang ở PendingApproval hoặc Draft
            if (string.Equals(profile.Profilestatus, TutorProfileStatus.Active, StringComparison.OrdinalIgnoreCase))
                return false;

            var user     = await _unitOfWork.UserRepository.GetUserByIdAsync(tutorId);
            var subjects = await _unitOfWork.TutorRepository.GetTutorSubjectsByTutorIdAsync(tutorId);
            var prices   = await _unitOfWork.TutorRepository.GetTutorSubjectGradePricesAsync(tutorId);

            bool identityVerified = user?.Isidentityverified ?? false;
            bool hasRequiredFields = CheckRequiredFields(profile, user!, subjects, prices);

            if (identityVerified && hasRequiredFields)
            {
                profile.Profilestatus = TutorProfileStatus.Active;
                profile.Ispublic      = true;
                profile.Updatedat     = TimeZoneHelper.UtcNow;
                await _unitOfWork.SaveChangesAsync();

                _logger.LogInformation("Profile {TutorId} activated after admin approved certificate", tutorId);
                return true;
            }

            return false;
        }
    }
}
