using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Feedback
{
    public int Feedbackid { get; set; }

    public int? Bookingid { get; set; }

    public string? Fromuserid { get; set; }

    public string? Touserid { get; set; }

    public int? Rating { get; set; }

    public string? Comment { get; set; }

    public string? Replycomment { get; set; }

    public DateTime? Repliedat { get; set; }

    public bool? Isvisible { get; set; }

    public DateTime? Createdat { get; set; }

    public string? Feedbacktype { get; set; }

    public int? Lessonid { get; set; }

    // --- CÁC TRƯỜNG MỚI THÊM ---
    public string? InitialGoal { get; set; } // Mapping với initial_goal

    public string? ActualResult { get; set; } // Mapping với actual_result

    public string? CourseDuration { get; set; } // Mapping với course_duration
    // ---------------------------

    public virtual Booking? Booking { get; set; }

    public virtual User? Fromuser { get; set; }

    public virtual Lesson? Lesson { get; set; }

    public virtual User? Touser { get; set; }
}