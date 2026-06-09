using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using static MV.DomainLayer.Constants.LessonStatus;
using static MV.DomainLayer.Constants.PaymentStatus;

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
    /// Create feedback for a lesson
    /// </summary>
    public async Task<FeedbackListResponse> CreateFeedbackAsync(string fromUserId, CreateFeedbackRequest request)
    {
        if (request.FeedbackType == FeedbackType.EarlyTermination)
        {
            return await CreateEarlyTerminationFeedbackAsync(fromUserId, request);
        }

        // Validate lesson belongs to parent's student
        var studentIds = await _context.Studentprofiles
            .Where(s => s.Parentid == fromUserId)
            .Select(s => s.Studentid)
            .ToListAsync();

        var lesson = await _context.Lessons
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Lessonid == request.LessonId && studentIds.Contains(l.Studentid!))
            ?? throw new ArgumentException("Không tìm thấy buổi học hoặc bạn không có quyền truy cập");

        if (lesson.Status != Completed)
            throw new InvalidOperationException("Chỉ có thể đánh giá cho buổi học đã hoàn thành");

        // Check if feedback already exists
        var existingFeedback = await _context.Feedbacks
            .AnyAsync(f => f.Lessonid == request.LessonId);

        if (existingFeedback)
            throw new InvalidOperationException("Buổi học này đã được đánh giá rồi");

        var feedback = new Feedback
        {
            Lessonid = request.LessonId,
            Bookingid = lesson.Bookingid,
            Fromuserid = fromUserId,
            Touserid = lesson.Tutorid,
            Rating = request.Rating,
            Comment = request.Comment,
            Feedbacktype = string.IsNullOrEmpty(request.FeedbackType) ? FeedbackType.ParentToTutor : request.FeedbackType,
            Isvisible = true,
            Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        };

        _context.Feedbacks.Add(feedback);
        await _context.SaveChangesAsync();

        // Recalculate tutor rating
        if (lesson.Tutorid != null)
        {
            await RecalculateTutorRatingAsync(lesson.Tutorid);
        }

        _logger.LogInformation("Parent {ParentId} created feedback {FeedbackId} for lesson {LessonId}",
            fromUserId, feedback.Feedbackid, request.LessonId);

        return new FeedbackListResponse
        {
            FeedbackId = feedback.Feedbackid,
            LessonId = request.LessonId,
            Rating = request.Rating,
            Comment = request.Comment,
            ParentName = (await _context.Users.FindAsync(fromUserId))?.Fullname,
            IsVisible = true,
            CreatedAt = TimeZoneHelper.ToUserTime(feedback.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow)
        };
    }

    /// <summary>
    /// Reply to feedback (tutor)
    /// </summary>
    public async Task<FeedbackListResponse> ReplyFeedbackAsync(int feedbackId, string tutorId, ReplyFeedbackRequest request)
    {
        var feedback = await _context.Feedbacks
            .Include(f => f.Fromuser)
            .Include(f => f.Lesson)
                .ThenInclude(l => l!.Booking)
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
            RepliedAt = feedback.Repliedat.HasValue ? TimeZoneHelper.ToUserTime(feedback.Repliedat.Value) : (DateTime?)null,
            IsVisible = feedback.Isvisible ?? true,
            CreatedAt = TimeZoneHelper.ToUserTime(feedback.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow)
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
            .Include(f => f.Fromuser)
            .Include(f => f.Lesson)
                .ThenInclude(l => l!.Booking)
                    .ThenInclude(b => b!.Tutorsubjectgradeprice)
                        .ThenInclude(p => p!.Subject)
            .OrderByDescending(f => f.Createdat);

        var totalCount = await query.CountAsync();

        var rawFeedbacks = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => new
            {
                f.Feedbackid,
                f.Lessonid,
                f.Rating,
                f.Comment,
                ParentName = f.Fromuser!.Fullname,
                ParentAvatarUrl = f.Fromuser.Avatarurl,
                SubjectName = f.Lesson!.Booking!.Tutorsubjectgradeprice!.Subject!.Subjectname,
                Reply = f.Replycomment,
                RepliedAt = f.Repliedat,
                IsVisible = f.Isvisible,
                f.Createdat
            })
            .ToListAsync();

        var feedbacks = rawFeedbacks.Select(f => new FeedbackListResponse
        {
            FeedbackId = f.Feedbackid,
            LessonId = f.Lessonid,
            Rating = f.Rating ?? 0,
            Comment = f.Comment,
            ParentName = f.ParentName,
            ParentAvatarUrl = f.ParentAvatarUrl,
            SubjectName = f.SubjectName,
            Reply = f.Reply,
            RepliedAt = f.RepliedAt.HasValue ? TimeZoneHelper.ToUserTime(f.RepliedAt.Value) : (DateTime?)null,
            IsVisible = f.IsVisible ?? true,
            CreatedAt = TimeZoneHelper.ToUserTime(f.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow)
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
    /// Check if user can leave feedback for lesson
    /// </summary>
    public async Task<bool> CanLeaveFeedbackAsync(int lessonId, string userId)
    {
        var studentIds = await _context.Studentprofiles
            .Where(s => s.Parentid == userId)
            .Select(s => s.Studentid)
            .ToListAsync();

        var lesson = await _context.Lessons
            .FirstOrDefaultAsync(l => l.Lessonid == lessonId && studentIds.Contains(l.Studentid!));

        if (lesson == null) return false;
        if (lesson.Status != Completed) return false;

        var existingFeedback = await _context.Feedbacks.AnyAsync(f => f.Lessonid == lessonId);
        return !existingFeedback;
    }

    private async Task<FeedbackListResponse> CreateEarlyTerminationFeedbackAsync(string fromUserId, CreateFeedbackRequest request)
    {
        if (!request.BookingId.HasValue)
            throw new ArgumentException("Vui lòng cung cấp BookingId để đánh giá kết thúc sớm");

        var bookingId = request.BookingId.Value;

        var booking = await _context.Bookings
            .Include(b => b.Student)
            .FirstOrDefaultAsync(b => b.Bookingid == bookingId)
            ?? throw new ArgumentException("Không tìm thấy booking");

        var isOwner = booking.Parentid == fromUserId ||
                      booking.Studentid == fromUserId ||
                      (booking.Student != null && booking.Student.Linkeduserid == fromUserId);

        if (!isOwner)
            throw new ArgumentException("Bạn không có quyền đánh giá booking này");

        if (booking.Status != BookingStatus.Cancelled && booking.Status != BookingStatus.Completed && booking.Status != BookingStatus.PaymentTimeout)
            throw new InvalidOperationException("Booking phải ở trạng thái đã hủy, hoàn thành hoặc quá hạn thanh toán để đánh giá kết thúc sớm");

        if (booking.Paymentstatus != Escrowed)
            throw new InvalidOperationException("Booking phải được thanh toán (cọc hoặc đầy đủ) để có thể đánh giá");

        var existingFeedback = await _context.Feedbacks
            .AnyAsync(f => f.Bookingid == bookingId && f.Fromuserid == fromUserId && f.Feedbacktype == FeedbackType.EarlyTermination);

        if (existingFeedback)
            throw new InvalidOperationException("Bạn đã đánh giá kết thúc sớm cho booking này rồi");

        var feedback = new Feedback
        {
            Lessonid = null,
            Bookingid = bookingId,
            Fromuserid = fromUserId,
            Touserid = booking.Tutorid,
            Rating = request.Rating,
            Comment = request.Comment,
            Feedbacktype = FeedbackType.EarlyTermination,
            InitialGoal = request.InitialGoal,
            ActualResult = request.ActualResult,
            CourseDuration = request.CourseDuration,
            Isvisible = true,
            Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        };

        _context.Feedbacks.Add(feedback);
        await _context.SaveChangesAsync();

        if (booking.Tutorid != null)
        {
            await RecalculateTutorRatingAsync(booking.Tutorid);
        }

        _logger.LogInformation("User {UserId} created early termination feedback {FeedbackId} for booking {BookingId}",
            fromUserId, feedback.Feedbackid, bookingId);

        return new FeedbackListResponse
        {
            FeedbackId = feedback.Feedbackid,
            Rating = request.Rating,
            Comment = request.Comment,
            ParentName = (await _context.Users.FindAsync(fromUserId))?.Fullname,
            IsVisible = true,
            CreatedAt = TimeZoneHelper.ToUserTime(feedback.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow)
        };
    }

    public async Task<bool> CanLeaveBookingFeedbackAsync(int bookingId, string userId)
    {
        var booking = await _context.Bookings
            .Include(b => b.Student)
            .FirstOrDefaultAsync(b => b.Bookingid == bookingId);

        if (booking == null) return false;

        var isOwner = booking.Parentid == userId ||
                      booking.Studentid == userId ||
                      (booking.Student != null && booking.Student.Linkeduserid == userId);

        if (!isOwner) return false;

        if (booking.Status != BookingStatus.Cancelled && booking.Status != BookingStatus.Completed && booking.Status != BookingStatus.PaymentTimeout)
            return false;

        if (booking.Paymentstatus != Escrowed)
            return false;

        var existingFeedback = await _context.Feedbacks
            .AnyAsync(f => f.Bookingid == bookingId && f.Fromuserid == userId && f.Feedbacktype == FeedbackType.EarlyTermination);

        return !existingFeedback;
    }
}
