using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace MV.DomainLayer.Entities;

public partial class Lesson
{
    public int Lessonid { get; set; }

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

    public int? Originallessonid { get; set; }

    [NotMapped]
    public int? Originalessonid
    {
        get => Originallessonid;
        set => Originallessonid = value;
    }

    public string? Noshowaction { get; set; }

    public DateTime? Createdat { get; set; }

    public bool? Isearlysubmission { get; set; }

    public virtual Booking? Booking { get; set; }

    public virtual ICollection<Dispute> Disputes { get; set; } = new List<Dispute>();

    public virtual ICollection<Feedback> Feedbacks { get; set; } = new List<Feedback>();

    public virtual ICollection<Lesson> InverseOriginallesson { get; set; } = new List<Lesson>();

    public virtual Lessonreport? Lessonreport { get; set; }

    public virtual Lesson? Originallesson { get; set; }

    public virtual Studentprofile? Student { get; set; }

    public virtual Tutorprofile? Tutor { get; set; }
}
