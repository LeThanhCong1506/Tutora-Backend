using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.ApplicationLayer.Helpers;

namespace MV.ApplicationLayer.Services
{
    public partial class UserService
    {
        // ─── Tutor Profile & Scheduling ───────────────────────────────────────

        public async Task UpdateTutorProfileAsync(string userId, UpdateTutorProfileRequest request)
        {
            var profile = await _userRepository.GetTutorProfileByIdAsync(userId)
                ?? throw new KeyNotFoundException("Không tìm thấy hồ sơ gia sư.");

            bool hasCriticalChange = false;

            if (!string.IsNullOrEmpty(request.Headline) && profile.Headline != request.Headline)
            { profile.Headline = request.Headline; hasCriticalChange = true; }

            if (!string.IsNullOrEmpty(request.Bio) && profile.Bio != request.Bio)
            { profile.Bio = request.Bio; hasCriticalChange = true; }

            if (!string.IsNullOrEmpty(request.VideoIntroUrl) && profile.Videointrourl != request.VideoIntroUrl)
            { profile.Videointrourl = request.VideoIntroUrl; hasCriticalChange = true; }

            if (!string.IsNullOrEmpty(request.Education) && profile.Education != request.Education)
            { profile.Education = request.Education; hasCriticalChange = true; }

            if (!string.IsNullOrEmpty(request.Experience) && profile.Experience != request.Experience)
            { profile.Experience = request.Experience; hasCriticalChange = true; }

            if (request.Gpa.HasValue) profile.Gpa = request.Gpa.Value;
            if (request.GpaScale.HasValue) profile.Gpascale = request.GpaScale.Value;

            
            if (!string.IsNullOrEmpty(request.TeachingAreaCity)) profile.Teachingareacity = request.TeachingAreaCity;
            if (!string.IsNullOrEmpty(request.TeachingAreaDistrict)) profile.Teachingareadistrict = request.TeachingAreaDistrict;

            // State machine: Active + critical change → Draft; Rejected → PendingApproval
            if (string.Equals(profile.Profilestatus, TutorProfileStatus.Active, StringComparison.OrdinalIgnoreCase) && hasCriticalChange)
            {
                profile.Profilestatus = TutorProfileStatus.Draft;
                profile.Ispublic = false;
            }
            else if (string.Equals(profile.Profilestatus, TutorProfileStatus.Rejected, StringComparison.OrdinalIgnoreCase))
            {
                profile.Profilestatus = TutorProfileStatus.PendingApproval;
            }

            profile.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            await _userRepository.UpdateTutorProfileAsync(profile);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateTutorWeeklyAvailabilityAsync(string tutorId, UpdateTutorScheduleRequest request)
        {
            var tutorProfile = await _userRepository.GetTutorProfileByIdAsync(tutorId)
                ?? throw new KeyNotFoundException("Không tìm thấy hồ sơ gia sư để cập nhật lịch.");

            // Guard: chặn nếu còn buổi dạy đã đặt nằm NGOÀI lịch rảnh mới (tránh bỏ rơi buổi đã cam kết).
            var newSlots = request.ListOfFreeTimeSlots
                .Select(s => (IsoDay: s.DayOfWeek,
                              Start: TimeOnly.Parse(s.StartTime).ToTimeSpan(),
                              End: TimeOnly.Parse(s.EndTime).ToTimeSpan()))
                .ToList();
            var committed = await TutorScheduleGuard.GetFutureCommittedSessionsAsync(_context, tutorId);
            if (TutorScheduleGuard.HasOrphanedSession(committed, newSlots))
                throw new InvalidOperationException(
                    "Không thể cập nhật lịch: có buổi dạy đã được đặt nằm ngoài lịch rảnh mới. Vui lòng giữ lại khung giờ đã có buổi dạy.");

            await _userRepository.DeleteAllExistingAvailabilityByTutorIdAsync(tutorId);

            var availabilityEntities = new List<Tutoravailability>();
            foreach (var slot in request.ListOfFreeTimeSlots)
            {
                var startTime = TimeOnly.Parse(slot.StartTime);
                var endTime = TimeOnly.Parse(slot.EndTime);

                if (startTime >= endTime)
                {
                    throw new InvalidOperationException(
                        $"Slot không hợp lệ: Giờ bắt đầu ({slot.StartTime}) phải trước giờ kết thúc ({slot.EndTime}).");
                }

                availabilityEntities.Add(new Tutoravailability
                {
                    Tutorid = tutorId,
                    Dayofweek = slot.DayOfWeek,
                    Starttime = startTime,
                    Endtime = endTime,
                    Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                });
            }

            await _userRepository.CreateNewAvailabilitySlotsAsync(availabilityEntities);
            await _context.SaveChangesAsync();
        }

        public async Task AutoUpdateTutorProfileStatusAsync(string tutorId)
        {
            var profile = await _userRepository.GetTutorProfileByIdAsync(tutorId);
            if (profile == null) return;

            if (string.Equals(profile.Profilestatus, TutorProfileStatus.Active, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(profile.Profilestatus, TutorProfileStatus.Rejected, StringComparison.OrdinalIgnoreCase))
                return;

            var prices = await _tutorRepository.GetTutorSubjectGradePricesAsync(tutorId);

            bool hasBasicInfo = !string.IsNullOrWhiteSpace(profile.Headline) &&
                                !string.IsNullOrWhiteSpace(profile.Bio) &&
                                prices.Any(p => p.Isactive && p.Priceperhour > 0);

            bool hasSubjects = await _userRepository.CheckIfTutorHasAnySubjectAsync(tutorId);
            bool hasAvailability = await _userRepository.CheckIfTutorHasAnyAvailabilityAsync(tutorId);

            profile.Profilestatus = (hasBasicInfo && hasSubjects && hasAvailability)
                ? TutorProfileStatus.PendingApproval
                : TutorProfileStatus.Draft;

            await _userRepository.UpdateTutorProfileAsync(profile);
            await _context.SaveChangesAsync();
        }

        public async Task<ApproveTutorResponse> ApproveTutorProfileAsync(string tutorId, ApproveTutorRequest request, string adminId)
        {
            var profile = await _userRepository.GetTutorProfileByIdAsync(tutorId)
                ?? throw new KeyNotFoundException("Không tìm thấy hồ sơ gia sư.");

            var user = await _userRepository.GetUserByIdAsync(tutorId)
                ?? throw new UserNotFoundException();

            string statusText;
            if (request.IsApproved)
            {
                profile.Rejectionnote = null;
                profile.Reviewedby = adminId;
                profile.Reviewedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

                // Approve all pending certificates in bulk
                var certificates = await _tutorRepository.GetCertificatesByTutorIdAsync(tutorId);
                if (certificates != null)
                {
                    foreach (var cert in certificates.Where(c =>
                        string.Equals(c.Verificationstatus, CertificateStatus.PendingReview, StringComparison.OrdinalIgnoreCase)))
                    {
                        cert.Verificationstatus = CertificateStatus.Verified;
                        cert.Verificationnote = ApprovalStatusText.NoteApprovedByAdmin;
                        cert.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
                    }
                }

                // Admin explicitly approved → always activate, no secondary check
                profile.Profilestatus = TutorProfileStatus.Active;
                profile.Ispublic = true;
                statusText = ApprovalStatusText.Approved;
            }
            else
            {
                profile.Profilestatus = TutorProfileStatus.Rejected;
                profile.Ispublic = false;
                profile.Rejectionnote = request.Reason;
                profile.Reviewedby = adminId;
                profile.Reviewedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
                statusText = ApprovalStatusText.Rejected;
            }

            profile.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            await _userRepository.UpdateTutorProfileAsync(profile);
            await _context.SaveChangesAsync();

            _embedQueue.Enqueue(tutorId);

            // Send notification
            try
            {
                if (statusText == ApprovalStatusText.Approved)
                {
                    await _notificationService.CreateNotificationAsync(new NotificationRequest
                    {
                        Userid = tutorId,
                        Title = "Hồ sơ đã được duyệt!",
                        Message = "Chúc mừng! Hồ sơ gia sư của bạn đã được admin phê duyệt. Bạn đã xuất hiện trên marketplace và có thể nhận học sinh.",
                        Type = NotificationType.TutorVettingApproved
                    });
                }
                else if (statusText == ApprovalStatusText.ApprovedPendingProfile)
                {
                    var subjects = await _tutorRepository.GetTutorSubjectsByTutorIdAsync(tutorId);
                    var missing = new List<string>();
                    var prices = await _tutorRepository.GetTutorSubjectGradePricesAsync(tutorId);
                    if (!prices.Any(p => p.Isactive && p.Priceperhour > 0)) missing.Add("giá theo giờ");
                    if (string.IsNullOrWhiteSpace(profile.Bio)) missing.Add("giới thiệu bản thân");
                    if (string.IsNullOrWhiteSpace(profile.Videointrourl)) missing.Add("video giới thiệu");
                    if (string.IsNullOrWhiteSpace(user.Avatarurl)) missing.Add("ảnh đại diện");
                    if (string.IsNullOrWhiteSpace(profile.Education)) missing.Add("học vấn");
                    if (string.IsNullOrWhiteSpace(profile.Headline)) missing.Add("tiêu đề");
                    if (subjects == null || subjects.Count == 0) missing.Add("môn học");

                    var missingText = missing.Count > 0 ? string.Join(", ", missing) : "một số thông tin";
                    await _notificationService.CreateNotificationAsync(new NotificationRequest
                    {
                        Userid = tutorId,
                        Title = "Admin đã duyệt chứng chỉ — vui lòng hoàn tất hồ sơ",
                        Message = $"Chứng chỉ của bạn đã được phê duyệt. Vui lòng cập nhật: {missingText} để xuất hiện trên marketplace.",
                        Type = NotificationType.TutorVettingApproved
                    });
                }
                else if (statusText == ApprovalStatusText.Rejected)
                {
                    await _notificationService.CreateNotificationAsync(new NotificationRequest
                    {
                        Userid = tutorId,
                        Title = "Hồ sơ bị từ chối",
                        Message = $"Hồ sơ gia sư của bạn đã bị từ chối. Lý do: {request.Reason ?? "Không đạt yêu cầu"}. Vui lòng cập nhật lại và gửi lại để được xem xét.",
                        Type = NotificationType.TutorVettingRejected
                    });
                }
            }
            catch (Exception)
            {
                // Don't fail the approval if notification fails
            }

            return new ApproveTutorResponse
            {
                TutorName = user.Fullname ?? DisplayValues.Unknown,
                IsApproved = statusText,
                Reason = request.Reason
            };
        }

        // ─── Private helpers ─────────────────────────────────────────────────

        private static bool CheckTutorRequiredFields(
            Tutorprofile profile,
            User user,
            List<Tutorsubject>? subjects,
            List<Tutorsubjectgradeprice>? prices)
        {
            return !string.IsNullOrWhiteSpace(profile.Headline) &&
                   !string.IsNullOrWhiteSpace(profile.Teachingareacity) &&
                   
                   subjects != null && subjects.Count > 0 &&
                   !string.IsNullOrWhiteSpace(profile.Bio) &&
                   !string.IsNullOrWhiteSpace(profile.Education) &&
                   prices != null && prices.Any(p => p.Isactive && p.Priceperhour > 0) &&
                   !string.IsNullOrWhiteSpace(user.Avatarurl) &&
                   !string.IsNullOrWhiteSpace(profile.Videointrourl);
        }

        private static bool IsProfileReadyForReview(Tutorprofile p) =>
            !string.IsNullOrWhiteSpace(p.Headline) &&
            !string.IsNullOrWhiteSpace(p.Bio) &&
            !string.IsNullOrWhiteSpace(p.Teachingareacity) &&
            true;
    }
}
