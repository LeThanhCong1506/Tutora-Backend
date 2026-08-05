using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;
using MV.ApplicationLayer.Interfaces;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Service for managing feedback and ratings
/// </summary>
public class FeedbackService : IFeedbackService
{
    private readonly IAppDbContext _context;
    private readonly ILogger<FeedbackService> _logger;

    public FeedbackService(IAppDbContext context, ILogger<FeedbackService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Create the booking review. Người học đánh giá một lần cho cả khóa sau khi booking hoàn thành.
    /// </summary>
    public async Task<FeedbackListResponse> CreateFeedbackAsync(string fromUserId, CreateFeedbackRequest request)
    {
        var bookingId = request.BookingId;

        var booking = await _context.Bookings
            .Include(b => b.Student)
            .FirstOrDefaultAsync(b => b.Bookingid == bookingId)
            ?? throw new ArgumentException("Không tìm thấy khóa học");

        if (!CanReviewBooking(booking, fromUserId))
            throw new ArgumentException("Bạn không có quyền đánh giá khóa học này");

        if (booking.Status != BookingStatus.Completed)
            throw new InvalidOperationException("Chỉ có thể đánh giá khi khóa học đã hoàn thành");

        // Chặn theo booking chứ không theo người: một booking chỉ đóng góp đúng một điểm cho
        // gia sư. Chặt hơn ràng buộc UNIQUE(booking_id, from_user_id) ở DB, đồng thời loại luôn
        // trường hợp booking đã có đánh giá theo buổi từ dữ liệu cũ.
        var existingFeedback = await _context.Feedbacks
            .AnyAsync(f => f.Bookingid == bookingId);

        if (existingFeedback)
            throw new InvalidOperationException("Khóa học này đã được đánh giá rồi");

        var feedback = new Feedback
        {
            Classsessionid = null,
            Bookingid = bookingId,
            Fromuserid = fromUserId,
            Touserid = booking.Tutorid,
            Rating = request.Rating,
            Comment = request.Comment,
            Feedbacktype = FeedbackType.BookingReview,
            InitialGoal = request.InitialGoal,
            ActualResult = request.ActualResult,
            CourseDuration = request.CourseDuration,
            Isvisible = true,
            Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        };

        _context.Feedbacks.Add(feedback);
        await _context.SaveChangesAsync();

        // Recalculate tutor rating
        if (booking.Tutorid != null)
        {
            await RecalculateTutorRatingAsync(booking.Tutorid);
        }

        _logger.LogInformation("User {UserId} created booking review {FeedbackId} for booking {BookingId}",
            fromUserId, feedback.Feedbackid, bookingId);

        return new FeedbackListResponse
        {
            FeedbackId = feedback.Feedbackid,
            BookingId = bookingId,
            Rating = request.Rating,
            Comment = request.Comment,
            FeedbackType = FeedbackType.BookingReview,
            InitialGoal = request.InitialGoal,
            ActualResult = request.ActualResult,
            CourseDuration = request.CourseDuration,
            ParentName = (await _context.Users.FindAsync(fromUserId))?.Fullname,
            IsVisible = true,
            CreatedAt = feedback.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        };
    }

    /// <summary>
    /// Reply to feedback (tutor)
    /// </summary>
    public async Task<FeedbackListResponse> ReplyFeedbackAsync(int feedbackId, string tutorId, ReplyFeedbackRequest request)
    {
        var feedback = await _context.Feedbacks
            .Include(f => f.Fromuser)
            .FirstOrDefaultAsync(f => f.Feedbackid == feedbackId && f.Touserid == tutorId)
            ?? throw new ArgumentException("Không tìm thấy đánh giá hoặc bạn không có quyền thực hiện");

        if (!string.IsNullOrWhiteSpace(feedback.Replycomment))
            throw new InvalidOperationException("Đánh giá này đã được trả lời rồi");

        feedback.Replycomment = request.ReplyComment;
        feedback.Repliedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Tutor {TutorId} replied to feedback {FeedbackId}", tutorId, feedbackId);

        return new FeedbackListResponse
        {
            FeedbackId = feedback.Feedbackid,
            Rating = feedback.Rating ?? 0,
            Comment = feedback.Comment,
            ParentName = feedback.Fromuser?.Fullname,
            Reply = feedback.Replycomment,
            RepliedAt = feedback.Repliedat,
            IsVisible = feedback.Isvisible ?? true,
            CreatedAt = feedback.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        };
    }

    /// <summary>
    /// Get feedback for a tutor (public view)
    /// </summary>
    public async Task<PagedList<FeedbackListResponse>> GetTutorFeedbacksAsync(string tutorId, int page, int pageSize)
    {
        var query = _context.Feedbacks
            .AsNoTracking()
            .Where(f => f.Touserid == tutorId && f.Isvisible == true)
            .OrderByDescending(f => f.Createdat);

        var totalCount = await query.CountAsync();

        var rawFeedbacks = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => new
            {
                f.Feedbackid,
                f.Bookingid,
                f.Classsessionid,
                f.Rating,
                f.Comment,
                f.Feedbacktype,
                ParentName = f.Fromuser!.Fullname,
                ParentAvatarUrl = f.Fromuser.Avatarurl,
                // Lấy môn từ booking chứ không từ class session: đánh giá khóa học không gắn
                // với buổi nào nên đi qua ClassSession sẽ luôn ra null.
                SubjectName = f.Booking!.Tutorsubjectgradeprice!.Subject!.Subjectname,
                f.InitialGoal,
                f.ActualResult,
                f.CourseDuration,
                Reply = f.Replycomment,
                RepliedAt = f.Repliedat,
                IsVisible = f.Isvisible,
                f.Createdat
            })
            .ToListAsync();

        var feedbacks = rawFeedbacks.Select(f => new FeedbackListResponse
        {
            FeedbackId = f.Feedbackid,
            BookingId = f.Bookingid,
            ClassSessionId = f.Classsessionid,
            Rating = f.Rating ?? 0,
            Comment = f.Comment,
            FeedbackType = f.Feedbacktype,
            ParentName = f.ParentName,
            ParentAvatarUrl = f.ParentAvatarUrl,
            SubjectName = f.SubjectName,
            InitialGoal = f.InitialGoal,
            ActualResult = f.ActualResult,
            CourseDuration = f.CourseDuration,
            Reply = f.Reply,
            RepliedAt = f.RepliedAt,
            IsVisible = f.IsVisible ?? true,
            CreatedAt = f.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        }).ToList();

        return new PagedList<FeedbackListResponse>(feedbacks, totalCount, page, pageSize);
    }

    /// <summary>
    /// Get feedback statistics for a tutor
    /// </summary>
    public async Task<FeedbackStatsResponse> GetTutorFeedbackStatsAsync(string tutorId)
    {
        var ratings = await _context.Feedbacks
            .AsNoTracking()
            .Where(f => f.Touserid == tutorId && f.Isvisible == true)
            .Select(f => f.Rating ?? 0)
            .ToListAsync();

        if (!ratings.Any())
        {
            return new FeedbackStatsResponse
            {
                TutorId = tutorId,
                TotalReviews = 0,
                AverageRating = 0
            };
        }

        var totalReviews = ratings.Count;
        var averageRating = ratings.Average();

        return new FeedbackStatsResponse
        {
            TutorId = tutorId,
            TotalReviews = totalReviews,
            AverageRating = Math.Round(averageRating, 1),
            Rating5Count = ratings.Count(r => r == 5),
            Rating4Count = ratings.Count(r => r == 4),
            Rating3Count = ratings.Count(r => r == 3),
            Rating2Count = ratings.Count(r => r == 2),
            Rating1Count = ratings.Count(r => r == 1),
            Rating5Percent = Math.Round((double)ratings.Count(r => r == 5) / totalReviews * 100, 1),
            Rating4Percent = Math.Round((double)ratings.Count(r => r == 4) / totalReviews * 100, 1),
            Rating3Percent = Math.Round((double)ratings.Count(r => r == 3) / totalReviews * 100, 1),
            Rating2Percent = Math.Round((double)ratings.Count(r => r == 2) / totalReviews * 100, 1),
            Rating1Percent = Math.Round((double)ratings.Count(r => r == 1) / totalReviews * 100, 1)
        };
    }

    /// <summary>
    /// Toggle feedback visibility (admin)
    /// </summary>
    public async Task<bool> ToggleFeedbackVisibilityAsync(int feedbackId, string adminId)
    {
        var feedback = await _context.Feedbacks.FindAsync(feedbackId)
            ?? throw new ArgumentException("Không tìm thấy đánh giá");

        feedback.Isvisible = !(feedback.Isvisible ?? true);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Admin {AdminId} toggled visibility of feedback {FeedbackId} to {Visible}",
            adminId, feedbackId, feedback.Isvisible);

        // Recalculate tutor rating if visibility changed
        if (feedback.Touserid != null)
        {
            await RecalculateTutorRatingAsync(feedback.Touserid);
        }

        return feedback.Isvisible ?? true;
    }

    /// <summary>
    /// Recalculate tutor's average rating
    /// </summary>
    public async Task RecalculateTutorRatingAsync(string tutorId)
    {
        var ratings = await _context.Feedbacks
            .Where(f => f.Touserid == tutorId && f.Isvisible == true && f.Rating.HasValue)
            .Select(f => f.Rating!.Value)
            .ToListAsync();

        var tutorProfile = await _context.Tutorprofiles.FirstOrDefaultAsync(t => t.Tutorid == tutorId);
        if (tutorProfile == null) return;

        if (ratings.Any())
        {
            tutorProfile.Averagerating = Math.Round(ratings.Average(), 1);
            tutorProfile.Totalreviews = ratings.Count;
        }
        else
        {
            tutorProfile.Averagerating = 0;
            tutorProfile.Totalreviews = 0;
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Recalculated rating for tutor {TutorId}: {Rating} ({Reviews} reviews)",
            tutorId, tutorProfile.Averagerating, tutorProfile.Totalreviews);
    }

    /// <summary>
    /// Check if user can review a booking. Cùng bộ điều kiện với <see cref="CreateFeedbackAsync"/>
    /// để nút đánh giá trên FE không bao giờ hiện ra rồi submit lỗi.
    /// </summary>
    public async Task<bool> CanLeaveBookingFeedbackAsync(int bookingId, string userId)
    {
        var booking = await _context.Bookings
            .Include(b => b.Student)
            .FirstOrDefaultAsync(b => b.Bookingid == bookingId);

        if (booking == null) return false;

        if (!CanReviewBooking(booking, userId)) return false;

        if (booking.Status != BookingStatus.Completed) return false;

        var existingFeedback = await _context.Feedbacks
            .AnyAsync(f => f.Bookingid == bookingId);

        return !existingFeedback;
    }

    /// <summary>
    /// Ai được đánh giá booking. Mỗi booking chỉ sinh ra đúng một điểm cho gia sư, nên khi
    /// booking có phụ huynh thì chỉ phụ huynh đánh giá — học sinh liên kết không đánh giá thêm
    /// lần nữa. Booking do học sinh tự đăng ký đặt (<c>Parentid</c> null) thì chính học sinh đánh giá.
    /// </summary>
    private static bool CanReviewBooking(Booking booking, string userId)
        => string.IsNullOrEmpty(booking.Parentid)
            ? booking.Studentid == userId ||
              (booking.Student != null && booking.Student.Linkeduserid == userId)
            : booking.Parentid == userId;
}
