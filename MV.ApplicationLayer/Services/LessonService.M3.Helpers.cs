using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using System.Text.Json;
namespace MV.ApplicationLayer.Services;

public partial class LessonService
{
    // ── Shared helpers used by Calendar, Attendance, and NoShow partials ──────

    private static LessonDetailResponse MapToLessonDetailResponse(Lesson lesson)
    {
        return new LessonDetailResponse
        {
            LessonId = lesson.Lessonid,
            BookingId = lesson.Bookingid,
            // Tất cả datetime trả về theo giờ Việt Nam (UTC+7) để frontend hiển thị đúng
            ScheduledStart = MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(lesson.Scheduledstart),
            ScheduledEnd = MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(lesson.Scheduledend),
            RealStart = MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(lesson.Realstart),
            RealEnd = MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(lesson.Realend),
            CheckInTime = MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(lesson.Checkintime),
            CheckOutTime = MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(lesson.Checkouttime),
            IsTutorPresent = lesson.Istutorpresent,
            IsStudentPresent = lesson.Isstudentpresent,
            AttendanceNote = lesson.Attendancenote,
            Status = lesson.Status,
            SubmittedAt = MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(lesson.Submittedat),
            ConfirmDeadline = MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(lesson.Confirmdeadline),
            ParentAckAt = MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(lesson.Parentackat),
            IsSettled = lesson.Issettled,
            LessonContent = lesson.Lessoncontent,
            Homework = lesson.Homework,
            TutorNotes = lesson.Tutornotes,
            MeetingLink = lesson.Meetinglink,
            LessonPrice = lesson.Lessonprice,
            IsMakeup = lesson.Ismakeup,
            OriginalLessonId = lesson.Originalessonid,
            NoShowAction = lesson.Noshowaction,
            Student = lesson.Booking?.Student != null ? new LessonStudentResponse
            {
                StudentId = lesson.Booking.Student.Studentid,
                FullName = lesson.Booking.Student.Fullname,
                School = lesson.Booking.Student.School,
                GradeLevel = lesson.Booking.Student.Gradelevel
            } : null,
            Tutor = lesson.Tutor?.Tutor != null ? new LessonTutorResponse
            {
                TutorId = lesson.Tutor.Tutorid,
                FullName = lesson.Tutor.Tutor.Fullname,
                AvatarUrl = lesson.Tutor.Tutor.Avatarurl,
                AverageRating = lesson.Tutor.Averagerating
            } : null,
            Subject = lesson.Booking?.Tutorsubjectgradeprice?.Subject != null ? new LessonSubjectResponse
            {
                SubjectId = lesson.Booking.Tutorsubjectgradeprice.Subject.Subjectid,
                SubjectName = lesson.Booking.Tutorsubjectgradeprice.Subject.Subjectname
            } : null,
            Report = lesson.Lessonreport != null ? new LessonReportResponse
            {
                ReportId = lesson.Lessonreport.Reportid,
                ContentCovered = lesson.Lessonreport.Contentcovered,
                HomeworkAssigned = lesson.Lessonreport.Homeworkassigned,
                StudentPerformanceRating = lesson.Lessonreport.Studentperformancerating,
                Attachments = DeserializeAttachments(lesson.Lessonreport.Attachments),
                CreatedAt = lesson.Lessonreport.Createdat.HasValue ? MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(lesson.Lessonreport.Createdat.Value) : (DateTime?)null
            } : null
        };
    }

    /// <summary>
    /// Deserialize attachments from JSON array or legacy comma-separated format.
    /// </summary>
    private static List<string>? DeserializeAttachments(string? attachments)
    {
        if (string.IsNullOrWhiteSpace(attachments)) return null;

        // Try JSON array first (new format)
        if (attachments.TrimStart().StartsWith('['))
        {
            try
            {
                return JsonSerializer.Deserialize<List<string>>(attachments);
            }
            catch
            {
                // Fall through to legacy format
            }
        }

        // Legacy comma-separated format
        return attachments.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
