using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class ClassSession
{
    public int Classsessionid { get; set; }

    public int? Bookingid { get; set; }

    public string? Tutorid { get; set; }

    public string? Studentid { get; set; }

    public DateTime Scheduledstart { get; set; }

    public DateTime Scheduledend { get; set; }

    public DateTime? Realstart { get; set; }

    public DateTime? Realend { get; set; }

    public string? Meetinglink { get; set; }

    public bool? Istutorpresent { get; set; }

    public bool? Isstudentpresent { get; set; }

    public string? Attendancenote { get; set; }

    public string? Status { get; set; }

    public DateTime? Submittedat { get; set; }

    public DateTime? Confirmdeadline { get; set; }

    public DateTime? Receiptsentat { get; set; }

    public DateTime? Parentackat { get; set; }

    public decimal? Lessonprice { get; set; }

    public bool? Issettled { get; set; }

    public DateTime? Checkintime { get; set; }

    public DateTime? Checkouttime { get; set; }

    public string? Lessoncontent { get; set; }

    public string? Homework { get; set; }

    public string? Tutornotes { get; set; }

    public bool? Autoreportsent { get; set; }

    public DateTime? Autoreportsentat { get; set; }

    public bool? Ismakeup { get; set; }

    public int? Originalsessionid { get; set; }

    /// <summary>Buổi phụ được sinh ra khi buổi gốc (Originalsessionid) bị ngắt giữa chừng vì sự cố đột xuất.</summary>
    public bool Iscontinuation { get; set; }

    /// <summary>Buổi học lại được Admin/Staff mở khi hai bên hoà giải một tranh chấp (xem CloseDisputeAsync) trên buổi gốc (Originalsessionid).</summary>
    public bool Isdisputerelearn { get; set; }

    /// <summary>Mốc thời điểm buổi gốc bị ngắt giữa chừng (UTC). Null nếu buổi chưa từng bị ngắt.</summary>
    public DateTime? Interruptedat { get; set; }

    /// <summary>Lý do ngắt buổi do người dùng tự nhập, optional.</summary>
    public string? Interruptreason { get; set; }

    /// <summary>User_id của người báo ngắt (gia sư/học sinh/phụ huynh). Không trả thẳng ra API —
    /// tầng response chỉ expose tên đã resolve qua <see cref="InterruptedbyNavigation"/>.</summary>
    public string? Interruptedby { get; set; }

    public virtual User? InterruptedbyNavigation { get; set; }

    /// <summary>Gắn trên chính BUỔI PHỤ (Iscontinuation=true) — mốc gia sư xác nhận đồng ý bỏ hẳn
    /// buổi phụ này (không học nốt phần còn lại). Null nếu chưa xác nhận.</summary>
    public DateTime? Tutorskipconfirmedat { get; set; }

    /// <summary>Gắn trên chính BUỔI PHỤ — mốc học sinh/phụ huynh xác nhận đồng ý bỏ hẳn buổi phụ
    /// này. Khi cả 2 cột này cùng có giá trị, SubmitReportAsync mới nhận báo cáo cho buổi GỐC
    /// (đang ở status=interrupted) và tự huỷ buổi phụ này. Null nếu chưa xác nhận.</summary>
    public DateTime? Studentskipconfirmedat { get; set; }

    public string? Noshowaction { get; set; }

    public DateTime? Createdat { get; set; }

    public bool? Isearlysubmission { get; set; }

    /// <summary>resourceId của phiên Cloud Recording (từ acquire) — cần để gọi stop.</summary>
    public string? Recordingresourceid { get; set; }

    /// <summary>sid của phiên Cloud Recording (từ start) — cần để gọi stop.</summary>
    public string? Recordingsid { get; set; }

    /// <summary>Link/đường dẫn file record sau khi stop (link Drive sau khi relay, hoặc link S3 tạm).</summary>
    public string? Recordingurl { get; set; }

    /// <summary>Object key của file trên S3 (kho đệm) — job relay dùng để đẩy lên Drive rồi xóa. Null sau khi đã relay.</summary>
    public string? Recordings3key { get; set; }

    /// <summary>UUID phòng Agora Interactive Whiteboard (Netless) của buổi học. Null nếu chưa mở bảng.</summary>
    public string? Whiteboardroomuuid { get; set; }

    public virtual Booking? Booking { get; set; }

    public virtual ICollection<Dispute> Disputes { get; set; } = new List<Dispute>();

    public virtual ICollection<Feedback> Feedbacks { get; set; } = new List<Feedback>();

    public virtual ICollection<ClassSessionScheduleChange> ScheduleChanges { get; set; } = new List<ClassSessionScheduleChange>();

    public virtual ICollection<ClassSessionRescheduleProposal> RescheduleProposals { get; set; } = new List<ClassSessionRescheduleProposal>();

    public virtual ICollection<ClassSessionAiJob> AiJobs { get; set; } = new List<ClassSessionAiJob>();

    public virtual ICollection<ClassSession> InverseOriginalsession { get; set; } = new List<ClassSession>();

    public virtual ClassSessionReport? ClassSessionReport { get; set; }

    public virtual ClassSession? Originalsession { get; set; }

    public virtual Studentprofile? Student { get; set; }

    public virtual Tutorprofile? Tutor { get; set; }
}
