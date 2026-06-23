using Microsoft.Extensions.Logging;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
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

            // 3. Gửi notification cho gia sư
            try
            {
                if (request.IsApproved)
                {
                    await _notificationService.CreateNotificationAsync(new NotificationRequest
                    {
                        Userid = tutorId,
                        Title = "Chứng chỉ được duyệt",
                        Message = $"Chứng chỉ \"{certificate.Certificatename}\" đã được admin phê duyệt."
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
                CertificateId = certificate.Certificateid,
                TutorId = certificate.Tutorid,
                CertificateName = certificate.Certificatename,
                VerificationStatus = certificate.Verificationstatus!,
                VerificationNote = certificate.Verificationnote,
                IsProfileActivated = false
            };
        }

        // ─── Admin: Danh sách chứng chỉ (có filter/search/paging) ──────────────

        public async Task<PagedList<PendingCertificateResponse>> GetAdminCertificatesAsync(CertificateParameters parameters)
        {
            var paged = await _unitOfWork.TutorRepository.GetAdminCertificatesAsync(parameters);

            var mapped = paged.Select(c => new PendingCertificateResponse
            {
                CertificateId = c.Certificateid,
                CertificateName = c.Certificatename,
                CertificateType = c.Certificatetype,
                IssuingOrganization = c.Issuingorganization,
                YearIssued = c.Yearissued,
                CertificateFileUrl = c.Certificatefileurl,
                VerificationStatus = c.Verificationstatus,
                CreatedAt = c.Createdat,
                TutorId = c.Tutorid,
                TutorFullName = c.Tutor?.Tutor?.Fullname,
                TutorEmail = c.Tutor?.Tutor?.Email,
                TutorAvatarUrl = c.Tutor?.Tutor?.Avatarurl
            }).ToList();

            return new PagedList<PendingCertificateResponse>(mapped, paged.TotalCount, paged.CurrentPage, paged.PageSize);
        }
    }
}
