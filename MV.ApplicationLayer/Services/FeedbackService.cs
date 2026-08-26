using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.DTO.ResponseModel.Admin;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Service for managing feedback and ratings
/// </summary>
public class FeedbackService : IFeedbackService
{
    private readonly IAppDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly ILogger<FeedbackService> _logger;

    public FeedbackService(
        IAppDbContext context,
        INotificationService notificationService,
        ILogger<FeedbackService> logger)
    {
        _context = context;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<FeedbackListResponse> CreateFeedbackAsync(string fromUserId, CreateFeedbackRequest request)
    {
        var bookingId = request.BookingId;

        var booking = await _context.Bookings
            .Include(b => b.Student)
            .FirstOrDefaultAsync(b => b.Bookingid == bookingId)
            ?? throw new ArgumentException("Không tìm thấy khóa học.");

        if (!CanReviewBooking(booking, fromUserId))
            throw new ArgumentException("Bạn không có quyền đánh giá khóa học này.");

        if (booking.Status != BookingStatus.Completed)
            throw new InvalidOperationException("Chỉ có thể đánh giá khi khóa học đã hoàn thành.");

        // Chặn theo booking chứ không theo người: một booking chỉ đóng góp đúng một điểm cho
        // gia sư. Chặt hơn ràng buộc UNIQUE(booking_id, from_user_id) ở DB, đồng thời loại luôn
        // trường hợp booking đã có đánh giá theo buổi từ dữ liệu cũ.
        var existingFeedback = await _context.Feedbacks
            .AnyAsync(f => f.Bookingid == bookingId);

        if (existingFeedback)
            throw new InvalidOperationException("Khóa học này đã được đánh giá rồi.");

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

        await NotifyFeedbackReceivedAsync(booking.Tutorid, booking.Bookingid, request.Rating);

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
            ReviewerRole = ReviewerRoleOf(booking.Parentid, fromUserId),
            IsVisible = true,
            CreatedAt = feedback.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        };
    }

    public async Task<FeedbackListResponse> ReplyFeedbackAsync(int feedbackId, string tutorId, ReplyFeedbackRequest request)
    {
        var feedback = await _context.Feedbacks
            .Include(f => f.Fromuser)
            .FirstOrDefaultAsync(f => f.Feedbackid == feedbackId && f.Touserid == tutorId)
            ?? throw new ArgumentException("Không tìm thấy đánh giá hoặc bạn không có quyền thực hiện.");

        if (!string.IsNullOrWhiteSpace(feedback.Replycomment))
            throw new InvalidOperationException("Đánh giá này đã được trả lời rồi.");

        feedback.Replycomment = request.ReplyComment;
        feedback.Repliedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Tutor {TutorId} replied to feedback {FeedbackId}", tutorId, feedbackId);

        await NotifyFeedbackRepliedAsync(feedback);

        return new FeedbackListResponse
        {
            FeedbackId = feedback.Feedbackid,
            BookingId = feedback.Bookingid,
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
    /// Báo cho gia sư biết vừa có đánh giá mới hoặc đánh giá cũ vừa được sửa. Dùng chung một
    /// <see cref="NotificationType.FeedbackReceived"/> vì đích đến giống nhau — danh sách booking
    /// của gia sư. Thông báo hỏng không được làm hỏng việc gửi/sửa đánh giá, nên nuốt lỗi và ghi log.
    /// </summary>
    private async Task NotifyFeedbackReceivedAsync(string? tutorId, int? bookingId, int rating, bool isUpdate = false)
    {
        if (string.IsNullOrEmpty(tutorId)) return;

        try
        {
            await _notificationService.CreateNotificationAsync(new NotificationRequest
            {
                Userid = tutorId,
                Title = isUpdate ? "Đánh giá của bạn vừa được cập nhật" : "Bạn nhận được đánh giá mới",
                Message = isUpdate
                    ? $"Người học đã sửa đánh giá cho khóa học #{bookingId} — hiện là {rating}/5 sao. Nhấn để xem."
                    : $"Người học vừa đánh giá {rating}/5 sao cho khóa học #{bookingId}. Nhấn để xem và phản hồi.",
                Type = NotificationType.FeedbackReceived,
                Referenceid = bookingId?.ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send feedback received notification for booking {BookingId}",
                bookingId);
        }
    }

    /// <summary>
    /// Báo cho người viết đánh giá biết gia sư đã phản hồi. Thông báo hỏng không được làm
    /// hỏng việc trả lời, nên nuốt lỗi và chỉ ghi log.
    /// </summary>
    private async Task NotifyFeedbackRepliedAsync(Feedback feedback)
    {
        if (string.IsNullOrEmpty(feedback.Fromuserid)) return;

        try
        {
            await _notificationService.CreateNotificationAsync(new NotificationRequest
            {
                Userid = feedback.Fromuserid,
                Title = "Gia sư đã phản hồi đánh giá của bạn.",
                Message = "Gia sư vừa trả lời đánh giá khóa học của bạn. Nhấn để xem phản hồi.",
                Type = NotificationType.FeedbackReply,
                Referenceid = feedback.Bookingid?.ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send feedback reply notification for feedback {FeedbackId}",
                feedback.Feedbackid);
        }
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
                f.Fromuserid,
                ParentName = f.Fromuser!.Fullname,
                ParentAvatarUrl = f.Fromuser.Avatarurl,
                BookingParentId = f.Booking!.Parentid,
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
            ReviewerRole = ReviewerRoleOf(f.BookingParentId, f.Fromuserid),
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
    public async Task<bool> ToggleFeedbackVisibilityAsync(int feedbackId, string adminId, string? reason = null)
    {
        var feedback = await _context.Feedbacks.FindAsync(feedbackId)
            ?? throw new ArgumentException("Không tìm thấy đánh giá.");

        var willBeVisible = !(feedback.Isvisible ?? true);

        // Ẩn thì bắt buộc có lý do — người bị ẩn phải biết vì sao.
        if (!willBeVisible && string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Vui lòng nhập lý do ẩn đánh giá.");

        feedback.Isvisible = willBeVisible;

        if (willBeVisible)
        {
            // Hiện lại thì xoá sạch dấu vết ẩn, tránh hiểu nhầm là vẫn đang bị ẩn.
            feedback.HiddenReason = null;
            feedback.HiddenAt = null;
            feedback.HiddenBy = null;
        }
        else
        {
            feedback.HiddenReason = reason!.Trim();
            feedback.HiddenAt = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            feedback.HiddenBy = adminId;
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Admin {AdminId} toggled visibility of feedback {FeedbackId} to {Visible}",
            adminId, feedbackId, feedback.Isvisible);

        // Recalculate tutor rating if visibility changed
        if (feedback.Touserid != null)
        {
            await RecalculateTutorRatingAsync(feedback.Touserid);
        }

        await NotifyFeedbackModeratedAsync(feedback, willBeVisible);

        return feedback.Isvisible ?? true;
    }

    /// <summary>
    /// Báo cho cả người viết đánh giá lẫn gia sư khi admin ẩn hoặc hiện lại một đánh giá.
    /// Hai bên nhận nội dung khác nhau: người viết cần biết lý do, gia sư cần biết điểm của
    /// mình vừa được tính lại. Nuốt lỗi để thao tác kiểm duyệt không bị hỏng vì thông báo.
    /// </summary>
    private async Task NotifyFeedbackModeratedAsync(Feedback feedback, bool isNowVisible)
    {
        var bookingRef = feedback.Bookingid?.ToString();

        var recipients = new List<(string? UserId, string Title, string Message)>
        {
            (feedback.Fromuserid,
                isNowVisible ? "Đánh giá của bạn đã được hiển thị lại" : "Đánh giá của bạn đã bị ẩn",
                isNowVisible
                    ? "Sau khi rà soát lại, đánh giá của bạn đã được hiển thị trở lại trên hồ sơ gia sư."
                    : $"Đánh giá của bạn đã bị ẩn khỏi hồ sơ gia sư. Lý do: {feedback.HiddenReason}"),

            (feedback.Touserid,
                isNowVisible ? "Một đánh giá về bạn đã hiển thị lại" : "Một đánh giá về bạn đã được ẩn",
                isNowVisible
                    ? "Quản trị viên đã hiển thị lại một đánh giá về bạn. Điểm đánh giá đã được tính lại."
                    : "Quản trị viên đã ẩn một đánh giá vi phạm chính sách về bạn. Điểm đánh giá đã được tính lại."),
        };

        foreach (var (userId, title, message) in recipients)
        {
            if (string.IsNullOrEmpty(userId)) continue;

            try
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = userId,
                    Title = title,
                    Message = message,
                    Type = NotificationType.FeedbackModerated,
                    Referenceid = bookingRef
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to send feedback moderation notification to {UserId} for feedback {FeedbackId}",
                    userId, feedback.Feedbackid);
            }
        }
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
    /// Đánh giá của một booking. Người đánh giá xem lại bài của mình, gia sư của booking xem
    /// đánh giá mình nhận được. Trả null nếu booking chưa được đánh giá.
    /// </summary>
    public async Task<FeedbackListResponse?> GetBookingFeedbackAsync(int bookingId, string userId)
    {
        var booking = await _context.Bookings
            .AsNoTracking()
            .Include(b => b.Student)
            .FirstOrDefaultAsync(b => b.Bookingid == bookingId)
            ?? throw new ArgumentException("Không tìm thấy khóa học.");

        var isTutorOfBooking = booking.Tutorid == userId;

        if (!CanReviewBooking(booking, userId) && !isTutorOfBooking)
            throw new ArgumentException("Bạn không có quyền xem đánh giá của khóa học này.");

        var feedback = await _context.Feedbacks
            .AsNoTracking()
            .Where(f => f.Bookingid == bookingId)
            .Select(f => new
            {
                f.Feedbackid,
                f.Bookingid,
                f.Classsessionid,
                f.Fromuserid,
                f.Rating,
                f.Comment,
                f.Feedbacktype,
                ParentName = f.Fromuser!.Fullname,
                ParentAvatarUrl = f.Fromuser.Avatarurl,
                SubjectName = f.Booking!.Tutorsubjectgradeprice!.Subject!.Subjectname,
                f.InitialGoal,
                f.ActualResult,
                f.CourseDuration,
                Reply = f.Replycomment,
                RepliedAt = f.Repliedat,
                IsVisible = f.Isvisible,
                f.HiddenReason,
                f.HiddenAt,
                f.Createdat
            })
            .FirstOrDefaultAsync();

        if (feedback == null) return null;

        return new FeedbackListResponse
        {
            FeedbackId = feedback.Feedbackid,
            BookingId = feedback.Bookingid,
            ClassSessionId = feedback.Classsessionid,
            Rating = feedback.Rating ?? 0,
            Comment = feedback.Comment,
            FeedbackType = feedback.Feedbacktype,
            ParentName = feedback.ParentName,
            ParentAvatarUrl = feedback.ParentAvatarUrl,
            ReviewerRole = ReviewerRoleOf(booking.Parentid, feedback.Fromuserid),
            SubjectName = feedback.SubjectName,
            InitialGoal = feedback.InitialGoal,
            ActualResult = feedback.ActualResult,
            CourseDuration = feedback.CourseDuration,
            Reply = feedback.Reply,
            RepliedAt = feedback.RepliedAt,
            IsVisible = feedback.IsVisible ?? true,
            HiddenReason = feedback.HiddenReason,
            HiddenAt = feedback.HiddenAt,
            CreatedAt = feedback.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
            // Chỉ tác giả sửa được, và chỉ tới khi gia sư phản hồi — sau đó khoá để câu trả lời
            // của gia sư không trở nên vô nghĩa.
            CanEdit = feedback.Fromuserid == userId && string.IsNullOrWhiteSpace(feedback.Reply)
        };
    }

    /// <summary>
    /// Sửa đánh giá. Chỉ tác giả sửa được, và chỉ khi gia sư chưa phản hồi.
    /// </summary>
    public async Task<FeedbackListResponse> UpdateFeedbackAsync(int feedbackId, string userId, UpdateFeedbackRequest request)
    {
        var feedback = await _context.Feedbacks
            .Include(f => f.Fromuser)
            .FirstOrDefaultAsync(f => f.Feedbackid == feedbackId)
            ?? throw new ArgumentException("Không tìm thấy đánh giá.");

        if (feedback.Fromuserid != userId)
            throw new ArgumentException("Bạn không có quyền sửa đánh giá này.");

        if (!string.IsNullOrWhiteSpace(feedback.Replycomment))
            throw new InvalidOperationException("Gia sư đã phản hồi, không thể sửa đánh giá.");

        feedback.Rating = request.Rating;
        feedback.Comment = request.Comment;
        feedback.InitialGoal = request.InitialGoal;
        feedback.ActualResult = request.ActualResult;
        feedback.CourseDuration = request.CourseDuration;

        await _context.SaveChangesAsync();

        // Đổi sao là đổi điểm gia sư trên marketplace.
        if (feedback.Touserid != null)
        {
            await RecalculateTutorRatingAsync(feedback.Touserid);
        }

        _logger.LogInformation("User {UserId} updated feedback {FeedbackId}", userId, feedbackId);

        await NotifyFeedbackReceivedAsync(feedback.Touserid, feedback.Bookingid, request.Rating, isUpdate: true);

        return new FeedbackListResponse
        {
            FeedbackId = feedback.Feedbackid,
            BookingId = feedback.Bookingid,
            Rating = request.Rating,
            Comment = request.Comment,
            FeedbackType = feedback.Feedbacktype,
            InitialGoal = request.InitialGoal,
            ActualResult = request.ActualResult,
            CourseDuration = request.CourseDuration,
            ParentName = feedback.Fromuser?.Fullname,
            IsVisible = feedback.Isvisible ?? true,
            CreatedAt = feedback.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
            CanEdit = true
        };
    }

    /// <summary>
    /// Danh sách đánh giá cho CMS kiểm duyệt — khác bản công khai ở chỗ trả cả đánh giá đã ẩn.
    /// </summary>
    public async Task<AdminFeedbackListResponse> GetFeedbacksForAdminAsync(
        string? tutorId, int? rating, bool? isVisible, int page, int pageSize)
    {
        var query = _context.Feedbacks.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(tutorId))
            query = query.Where(f => f.Touserid == tutorId);

        if (rating.HasValue)
            query = query.Where(f => f.Rating == rating.Value);

        if (isVisible.HasValue)
            query = query.Where(f => (f.Isvisible ?? true) == isVisible.Value);

        var totalCount = await query.CountAsync();

        // Thống kê tính trên toàn tập đã lọc, không phải trang hiện tại.
        var stats = await query
            .GroupBy(_ => 1)
            .Select(g => new AdminFeedbackStats
            {
                TotalCount = g.Count(),
                VisibleCount = g.Count(f => (f.Isvisible ?? true)),
                HiddenCount = g.Count(f => !(f.Isvisible ?? true)),
                AverageRating = g.Where(f => f.Rating.HasValue).Average(f => (double?)f.Rating) ?? 0
            })
            .FirstOrDefaultAsync() ?? new AdminFeedbackStats();

        var rawFeedbacks = await query
            .OrderByDescending(f => f.Createdat)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => new
            {
                f.Feedbackid,
                f.Bookingid,
                f.Rating,
                f.Comment,
                f.Feedbacktype,
                f.Fromuserid,
                ParentName = f.Fromuser!.Fullname,
                ParentAvatarUrl = f.Fromuser.Avatarurl,
                BookingParentId = f.Booking!.Parentid,
                TutorName = f.Touser!.Fullname,
                SubjectName = f.Booking!.Tutorsubjectgradeprice!.Subject!.Subjectname,
                f.InitialGoal,
                f.ActualResult,
                f.CourseDuration,
                Reply = f.Replycomment,
                RepliedAt = f.Repliedat,
                IsVisible = f.Isvisible,
                f.HiddenReason,
                f.HiddenAt,
                f.Createdat
            })
            .ToListAsync();

        var feedbacks = rawFeedbacks.Select(f => new FeedbackListResponse
        {
            FeedbackId = f.Feedbackid,
            BookingId = f.Bookingid,
            Rating = f.Rating ?? 0,
            Comment = f.Comment,
            FeedbackType = f.Feedbacktype,
            ParentName = f.ParentName,
            ParentAvatarUrl = f.ParentAvatarUrl,
            ReviewerRole = ReviewerRoleOf(f.BookingParentId, f.Fromuserid),
            TutorName = f.TutorName,
            HiddenReason = f.HiddenReason,
            HiddenAt = f.HiddenAt,
            SubjectName = f.SubjectName,
            InitialGoal = f.InitialGoal,
            ActualResult = f.ActualResult,
            CourseDuration = f.CourseDuration,
            Reply = f.Reply,
            RepliedAt = f.RepliedAt,
            IsVisible = f.IsVisible ?? true,
            CreatedAt = f.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        }).ToList();

        return new AdminFeedbackListResponse
        {
            Items = feedbacks,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            Stats = stats
        };
    }

    /// <summary>
    /// Ai được đánh giá booking. Mỗi booking chỉ sinh ra đúng một điểm cho gia sư, nên khi
    /// booking có phụ huynh thì chỉ phụ huynh đánh giá — học sinh liên kết không đánh giá thêm
    /// lần nữa. Booking do học sinh tự đăng ký đặt (<c>Parentid</c> null) thì chính học sinh đánh giá.
    /// </summary>
    private static string ReviewerRoleOf(string? bookingParentId, string? fromUserId)
        => !string.IsNullOrEmpty(bookingParentId) && bookingParentId == fromUserId
            ? UserRole.Parent.ToLowerInvariant()
            : UserRole.Student.ToLowerInvariant();

    private static bool CanReviewBooking(Booking booking, string userId)
        => string.IsNullOrEmpty(booking.Parentid)
            ? booking.Studentid == userId ||
              (booking.Student != null && booking.Student.Linkeduserid == userId)
            : booking.Parentid == userId;
}
