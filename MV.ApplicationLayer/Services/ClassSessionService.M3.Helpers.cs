using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;
namespace MV.ApplicationLayer.Services;

public partial class ClassSessionService
{
    // ── Shared helpers used by Calendar, Attendance, and NoShow partials ──────

    private static ClassSessionDetailResponse MapToClassSessionDetailResponse(ClassSession classSession)
    {
        var attachments = ReportAttachmentSerializer.Deserialize(classSession.ClassSessionReport?.Attachments);

        return new ClassSessionDetailResponse
        {
            ClassSessionId = classSession.Classsessionid,
            BookingId = classSession.Bookingid,
            // Tất cả datetime trả về theo giờ Việt Nam (UTC+7) để frontend hiển thị đúng
            ScheduledStart = classSession.Scheduledstart,
            ScheduledEnd = classSession.Scheduledend,
            RealStart = classSession.Realstart,
            RealEnd = classSession.Realend,
            CheckInTime = classSession.Checkintime,
            CheckOutTime = classSession.Checkouttime,
            IsTutorPresent = classSession.Istutorpresent,
            IsStudentPresent = classSession.Isstudentpresent,
            AttendanceNote = classSession.Attendancenote,
            Status = classSession.Status,
            BookingStatus = classSession.Booking?.Status,
            SubmittedAt = classSession.Submittedat,
            ConfirmDeadline = classSession.Confirmdeadline,
            ParentAckAt = classSession.Parentackat,
            IsSettled = classSession.Issettled,
            ClassSessionContent = classSession.Lessoncontent,
            Homework = classSession.Homework,
            TutorNotes = classSession.Tutornotes,
            MeetingLink = classSession.Meetinglink,
            ClassSessionPrice = classSession.Lessonprice,
            IsMakeup = classSession.Ismakeup,
            OriginalClassSessionId = classSession.Originalsessionid,
            NoShowAction = classSession.Noshowaction,
            Student = classSession.Booking?.Student != null ? new ClassSessionStudentResponse
            {
                StudentId = classSession.Booking.Student.Studentid,
                FullName = classSession.Booking.Student.Fullname,
                School = classSession.Booking.Student.School,
                GradeLevel = classSession.Booking.Student.Gradelevel
            } : null,
            Tutor = classSession.Tutor?.Tutor != null ? new ClassSessionTutorResponse
            {
                TutorId = classSession.Tutor.Tutorid,
                FullName = classSession.Tutor.Tutor.Fullname,
                AvatarUrl = classSession.Tutor.Tutor.Avatarurl,
                AverageRating = classSession.Tutor.Averagerating
            } : null,
            Subject = classSession.Booking?.Tutorsubjectgradeprice?.Subject != null ? new ClassSessionSubjectResponse
            {
                SubjectId = classSession.Booking.Tutorsubjectgradeprice.Subject.Subjectid,
                SubjectName = classSession.Booking.Tutorsubjectgradeprice.Subject.Subjectname
            } : null,
            Report = classSession.ClassSessionReport != null ? new ClassSessionReportResponse
            {
                ReportId = classSession.ClassSessionReport.Reportid,
                ContentCovered = classSession.ClassSessionReport.Contentcovered,
                HomeworkAssigned = classSession.ClassSessionReport.Homeworkassigned,
                StudentPerformanceRating = classSession.ClassSessionReport.Studentperformancerating,
                Attachments = ReportAttachmentSerializer.ToUrls(attachments),
                AttachmentDetails = attachments,
                CreatedAt = classSession.ClassSessionReport.Createdat.HasValue ? classSession.ClassSessionReport.Createdat.Value : (DateTime?)null
            } : null
        };
    }

}
