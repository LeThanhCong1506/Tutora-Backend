using Microsoft.EntityFrameworkCore;
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
            var certificate = await _tutorRepository.GetCertificateByIdAsync(certId)
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
            // KHÔNG nhúng adminId vào note — field này hiển thị trực tiếp cho Tutor xem
            // (GetVerificationProgressAsync → VerificationNote), lộ ID nội bộ của admin là
            // rò rỉ thông tin không cần thiết. "Admin nào duyệt" đã có trong log dưới đây.
            certificate.Verificationnote = noteText;
            certificate.Updatedat = TimeZoneHelper.UtcNow;

            await _context.SaveChangesAsync();

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
                        Message = $"Chứng chỉ \"{certificate.Certificatename}\" đã được admin phê duyệt.",
                        Type = NotificationType.TutorVettingApproved
                    });
                }
                else
                {
                    await _notificationService.CreateNotificationAsync(new NotificationRequest
                    {
                        Userid = tutorId,
                        Title = "Chứng chỉ bị từ chối",
                        Message = $"Chứng chỉ \"{certificate.Certificatename}\" đã bị từ chối. Lý do: {noteText}",
                        Type = NotificationType.TutorVettingRejected
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
                VerificationNote = certificate.Verificationnote
            };
        }

        // ─── Admin: Danh sách chứng chỉ (có filter/search/paging) ──────────────

        public async Task<PagedList<PendingCertificateResponse>> GetAdminCertificatesAsync(CertificateParameters parameters)
        {
            var paged = await _tutorRepository.GetAdminCertificatesAsync(parameters);

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

        // ─── Admin: Profile Update Requests (Tutor Active chỉnh sửa hồ sơ) ──────

        public async Task<List<PendingProfileUpdateRequestResponse>> GetPendingProfileUpdateRequestsAsync()
        {
            var tutorIds = await _updateStaging.GetAllPendingTutorIdsAsync();
            var result = new List<PendingProfileUpdateRequestResponse>();

            foreach (var tutorId in tutorIds)
            {
                // Bản pending có thể vừa bị Admin xử lý xong ở request khác (SMEMBERS đọc
                // trước, key đã bị xoá) — bỏ qua thay vì lỗi, danh sách sẽ tự cập nhật ở lần gọi sau.
                var response = await BuildPendingProfileUpdateRequestResponseAsync(tutorId);
                if (response != null) result.Add(response);
            }

            return result.OrderByDescending(r => r.SubmittedAt).ToList();
        }

        /// <summary>
        /// Bản mới nhất hiện tại của 1 request đang chờ duyệt — dùng để FE (CMS) tự kiểm tra
        /// "nội dung mình đang xem có còn đúng với nội dung sẽ thực sự được áp dụng không" ngay
        /// trước khi gửi lệnh Duyệt/Từ chối, phòng trường hợp Tutor vừa nộp thêm thay đổi trong
        /// lúc Admin đang xem mà màn hình không tự cập nhật (không có real-time push).
        /// Trả null nếu không còn request nào đang chờ cho tutor này (đã xử lý xong, hoặc chưa từng nộp).
        /// </summary>
        public Task<PendingProfileUpdateRequestResponse?> GetProfileUpdateRequestDetailAsync(string tutorId)
            => BuildPendingProfileUpdateRequestResponseAsync(tutorId);

        private async Task<PendingProfileUpdateRequestResponse?> BuildPendingProfileUpdateRequestResponseAsync(string tutorId)
        {
            var pending = await _updateStaging.GetPendingUpdateAsync(tutorId);
            if (pending == null) return null;

            var profile = await _tutorRepository.GetTutorProfileByIdAsync(tutorId);
            var user = await _userRepository.GetUserByIdAsync(tutorId);
            if (profile == null || user == null) return null;

            return new PendingProfileUpdateRequestResponse
            {
                TutorId = tutorId,
                TutorFullName = user.Fullname,
                TutorEmail = user.Email,
                TutorAvatarUrl = user.Avatarurl,
                SubmittedAt = pending.SubmittedAt,
                CurrentHeadline = profile.Headline,
                ProposedHeadline = pending.Headline,
                CurrentTeachingAreaCity = profile.Teachingareacity,
                ProposedTeachingAreaCity = pending.TeachingAreaCity,
                CurrentTeachingAreaDistrict = profile.Teachingareadistrict,
                ProposedTeachingAreaDistrict = pending.TeachingAreaDistrict,
                CurrentBio = profile.Bio,
                ProposedBio = pending.Bio,
                CurrentDegree = profile.Degree,
                ProposedDegree = pending.Degree,
                CurrentEducation = profile.Education,
                ProposedEducation = pending.Education,
                CurrentExperience = profile.Experience,
                ProposedExperience = pending.Experience,
                CurrentVideoIntroUrl = profile.Videointrourl,
                ProposedVideoIntroUrl = pending.VideoIntroUrl,
                HasProposedSubjectGradePrices = pending.SubjectGradePrices != null,
                CurrentSubjectGradePrices = profile.Tutorsubjectgradeprices
                    .Select(MapSubjectGradePriceResponse)
                    .ToList(),
                ProposedSubjectGradePrices = await MapPendingSubjectGradePricesAsync(pending.SubjectGradePrices)
            };
        }

        /// <summary>
        /// Bảng giá ĐỀ XUẤT chỉ có Id trần (SubjectId/GradeLevelId, xem <see cref="PendingTutorProfileUpdate"/>
        /// — JSON lưu Redis, không phải EF entity nên không có navigation Subject/Gradelevel như bản
        /// sống). Tra tên môn/khối lớp riêng ở đây để FE hiển thị được, không phải đoán qua ID.
        /// </summary>
        private async Task<List<TutorSubjectGradePriceResponse>> MapPendingSubjectGradePricesAsync(
            List<TutorSubjectGradePriceRequest>? prices)
        {
            if (prices == null || prices.Count == 0)
                return new List<TutorSubjectGradePriceResponse>();

            var subjectIds = prices.Select(p => p.SubjectId).Distinct().ToList();
            var gradeLevelIds = prices.Select(p => p.GradeLevelId).Distinct().ToList();

            var subjects = await _context.Subjects
                .Where(s => subjectIds.Contains(s.Subjectid))
                .ToDictionaryAsync(s => s.Subjectid);
            var gradeLevels = await _context.Gradelevels
                .Where(g => gradeLevelIds.Contains(g.Gradelevelid))
                .ToDictionaryAsync(g => g.Gradelevelid);

            return prices.Select(p =>
            {
                subjects.TryGetValue(p.SubjectId, out var subject);
                gradeLevels.TryGetValue(p.GradeLevelId, out var gradeLevel);
                return new TutorSubjectGradePriceResponse
                {
                    SubjectId = p.SubjectId,
                    SubjectName = subject?.Subjectname,
                    GradeLevelId = p.GradeLevelId,
                    GradeLevelName = gradeLevel?.Gradename,
                    PricePerHour = p.PricePerHour,
                    DurationMinutesPerSession = p.DurationMinutesPerSession,
                    SessionsPerWeek = p.SessionsPerWeek,
                    Currency = string.IsNullOrWhiteSpace(p.Currency) ? "VND" : p.Currency!,
                    IsActive = p.IsActive,
                    SubjectIsActive = subject?.IsActive ?? true,
                    GradeLevelIsActive = gradeLevel?.IsActive ?? true
                };
            }).ToList();
        }

        public async Task<ReviewProfileUpdateResponse> ReviewProfileUpdateRequestAsync(
            string tutorId, AdminReviewProfileUpdateRequest request, string adminId)
        {
            var (pending, rawJson) = await _updateStaging.GetPendingUpdateWithRawAsync(tutorId);
            if (pending == null || rawJson == null)
                throw new KeyNotFoundException(
                    "Yêu cầu cập nhật không còn tồn tại — có thể đã được xử lý trước đó hoặc dữ liệu tạm thời bị mất. Vui lòng yêu cầu tutor nộp lại.");

            var profile = await _tutorRepository.GetTutorProfileByIdAsync(tutorId)
                ?? throw new KeyNotFoundException("Không tìm thấy hồ sơ gia sư.");

            var user = await _userRepository.GetUserByIdAsync(tutorId)
                ?? throw new KeyNotFoundException("Không tìm thấy gia sư.");

            string statusText;
            bool clearedCleanly;

            if (request.IsApproved)
            {
                // Chỉ ghi đè field mà tutor thực sự có đề xuất (khác null) — field null nghĩa
                // là "không đụng tới ở lần nộp này", giữ nguyên giá trị đang có trên Tutorprofile.
                if (pending.Headline != null) profile.Headline = pending.Headline;
                if (pending.TeachingAreaCity != null) profile.Teachingareacity = pending.TeachingAreaCity;
                if (pending.TeachingAreaDistrict != null) profile.Teachingareadistrict = pending.TeachingAreaDistrict;
                if (pending.Bio != null) profile.Bio = pending.Bio;
                if (pending.Degree != null) profile.Degree = pending.Degree;
                if (pending.Education != null) profile.Education = pending.Education;
                if (pending.Gpa != null) profile.Gpa = pending.Gpa;
                if (pending.GpaScale != null) profile.Gpascale = pending.GpaScale;
                if (pending.Experience != null) profile.Experience = pending.Experience;
                if (pending.VideoIntroUrl != null) profile.Videointrourl = pending.VideoIntroUrl;
                profile.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

                if (pending.SubjectGradePrices != null)
                {
                    await ValidateSubjectGradePricesAsync(tutorId, pending.SubjectGradePrices);
                    await _tutorRepository.ReplaceTutorSubjectGradePricesAsync(
                        tutorId, MapSubjectGradePriceRequests(tutorId, pending.SubjectGradePrices));
                }

                await _context.SaveChangesAsync();

                // Staged edit của gia sư active vừa land vào Postgres → re-embed nền (bio/môn/
                // giá đổi). 
                _embedQueue.Enqueue(tutorId);

                // Compare-and-delete: chỉ xoá key Redis nếu nội dung vẫn giống hệt lúc đọc ở
                // trên (rawJson). Nếu Tutor vừa nộp thêm thay đổi mới trong lúc ta đang áp dụng
                // `pending` vào Postgres, key hiện tại đã khác → KHÔNG xoá, để bản mới nhất đó
                // tiếp tục nằm trong hàng chờ cho lần duyệt sau, tránh mất dữ liệu.
                clearedCleanly = await _updateStaging.ClearPendingUpdateIfUnchangedAsync(tutorId, rawJson);
                statusText = TutorProfileUpdateStatus.Approved;

                _logger.LogInformation("Admin {AdminId} approved profile update request for tutor {TutorId}", adminId, tutorId);
            }
            else
            {
                clearedCleanly = await _updateStaging.ClearPendingUpdateIfUnchangedAsync(tutorId, rawJson);
                statusText = TutorProfileUpdateStatus.Rejected;

                _logger.LogInformation("Admin {AdminId} rejected profile update request for tutor {TutorId}", adminId, tutorId);
            }

            try
            {
                if (request.IsApproved)
                {
                    await _notificationService.CreateNotificationAsync(new NotificationRequest
                    {
                        Userid = tutorId,
                        Title = "Cập nhật hồ sơ đã được duyệt",
                        Message = "Admin đã phê duyệt thay đổi hồ sơ của bạn. Thông tin mới đã hiển thị trên marketplace.",
                        Type = NotificationType.TutorVettingApproved
                    });
                }
                else
                {
                    await _notificationService.CreateNotificationAsync(new NotificationRequest
                    {
                        Userid = tutorId,
                        Title = "Cập nhật hồ sơ bị từ chối",
                        Message = $"Admin đã từ chối thay đổi hồ sơ của bạn. Lý do: {request.Note ?? "Không đạt yêu cầu"}. Hồ sơ hiện tại của bạn trên marketplace không bị ảnh hưởng.",
                        Type = NotificationType.TutorVettingRejected
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send profile-update review notification to tutor {TutorId}.", tutorId);
            }

            return new ReviewProfileUpdateResponse
            {
                TutorName = user.Fullname ?? DisplayValues.Unknown,
                Status = statusText,
                Note = request.Note,
                HasNewerPendingChanges = !clearedCleanly
            };
        }
    }
}
