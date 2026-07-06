using System;
using System.Collections.Generic;
using MV.DomainLayer.Enums;

namespace MV.DomainLayer.Entities;

public partial class User
{
    public string Userid { get; set; } = null!;

    public string? Username { get; set; }

    public string Password { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string? Phone { get; set; }

    public bool? Isphoneverified { get; set; }

    public string? Zalouserid { get; set; }

    public string? Fullname { get; set; }

    public DateOnly? Birthdate { get; set; }

    public Gender? Gender { get; set; }

    public string? Avatarurl { get; set; }

    public string? Address { get; set; }

    public string? Identitynumber { get; set; }

    public string? Idcardfronturl { get; set; }

    public string? Idcardbackurl { get; set; }

    public bool? Isidentityverified { get; set; }

    public int? Status { get; set; }

    public bool? Isemailverified { get; set; }

    public DateTime? Lastloginat { get; set; }

    public DateTime? Createdat { get; set; }

    public string? Ekycrawdata { get; set; }

    public string? Primaryrole { get; set; }

    public string? Googlecalendartoken { get; set; }

    public string? Fcmtoken { get; set; }

    public string? Parentcode { get; set; }

    public DateTime? Parentcodeexpiresat { get; set; }

    public bool? Zabornotifyenabled { get; set; }

    public bool? Hascompletedtour { get; set; }

    /// <summary>Người dùng tự tạm khóa tài khoản của mình (self-deactivation).</summary>
    public bool? Isdeactivated { get; set; }

    /// <summary>Thời điểm người dùng tự tạm khóa tài khoản.</summary>
    public DateTime? Deactivatedat { get; set; }

    /// <summary>Cache số dư AI credit hiện tại — nguồn chi tiết nằm ở <see cref="AiCreditTransaction"/>.</summary>
    public int AiCreditsBalance { get; set; }

    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    public virtual ICollection<AiCreditTransaction> AiCreditTransactions { get; set; } = new List<AiCreditTransaction>();

    public virtual ICollection<DisputeEvidence> DisputeEvidences { get; set; } = new List<DisputeEvidence>();

    public virtual ICollection<Chatmessage> Chatmessages { get; set; } = new List<Chatmessage>();

    public virtual ICollection<Dispute> DisputeCreatedbyNavigations { get; set; } = new List<Dispute>();

    public virtual ICollection<Dispute> DisputeResolvedbyNavigations { get; set; } = new List<Dispute>();

    public virtual ICollection<Feedback> FeedbackFromusers { get; set; } = new List<Feedback>();

    public virtual ICollection<Feedback> FeedbackTousers { get; set; } = new List<Feedback>();

    public virtual ICollection<Learningmaterial> Learningmaterials { get; set; } = new List<Learningmaterial>();

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual ICollection<Profilesuspension> ProfilesuspensionCreatedbyNavigations { get; set; } = new List<Profilesuspension>();

    public virtual ICollection<Profilesuspension> ProfilesuspensionUsers { get; set; } = new List<Profilesuspension>();

    public virtual ICollection<Studentprofile> StudentprofileLinkedusers { get; set; } = new List<Studentprofile>();

    public virtual ICollection<Studentprofile> StudentprofileParents { get; set; } = new List<Studentprofile>();

    public virtual ICollection<Systemconfig> Systemconfigs { get; set; } = new List<Systemconfig>();

    public virtual Tutorprofile? Tutorprofile { get; set; }



    public virtual ICollection<Userwarning> UserwarningIssuedbyNavigations { get; set; } = new List<Userwarning>();

    public virtual ICollection<Userwarning> UserwarningUsers { get; set; } = new List<Userwarning>();

    public virtual Wallet? Wallet { get; set; }

    public virtual ICollection<Withdrawalrequest> Withdrawalrequests { get; set; } = new List<Withdrawalrequest>();

    public virtual ICollection<Topuprequest> Topuprequests { get; set; } = new List<Topuprequest>();

    public virtual ICollection<Chatchannel> Channels { get; set; } = new List<Chatchannel>();

    public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
