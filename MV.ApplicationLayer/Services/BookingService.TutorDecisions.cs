using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services;

public partial class BookingService
{
    // ─── Tutor Accept / Decline ───────────────────────────────────────────

    public async Task<TutorDecisionResponse> AcceptBookingAsync(string tutorId, int bookingId)
    {
        Booking booking;
        var alreadyAccepted = false;

        await using (var tx = await context.Database.BeginTransactionAsync())
        {
            try
            {
                booking = await bookingRepo.FindWithRelationsForUpdateAsync(bookingId)
                    ?? throw new BookingException(BookingErrorCodes.BookingNotFound, "Không tìm thấy booking", 404);

                if (booking.Tutorid != tutorId)
                    throw new BookingException(BookingErrorCodes.NotBookingOwner, "Bạn không phải gia sư của booking này", 403);

                alreadyAccepted = booking.Status == BookingStatus.DepositPaid || booking.Status == BookingStatus.Paid;
                if (!alreadyAccepted)
                {
                    if (booking.Status != BookingStatus.PendingTutor)
                        throw new BookingException(BookingErrorCodes.InvalidBookingStatus, $"Không thể chấp nhận booking ở trạng thái '{booking.Status}'", 400);

                    if (booking.Responsedeadline != null && booking.Responsedeadline <= TimeZoneHelper.UtcNow)
                        throw new BookingException(
                            BookingErrorCodes.BookingExpired,
                            "Đã quá hạn phản hồi. Booking đang chờ hệ thống tự động hủy.",
                            409);

                    if (booking.Depositpaidat == null)
                        throw new BookingException(BookingErrorCodes.InvalidBookingStatus, "Booking chưa được thanh toán cọc", 409);

                    // Lưới an toàn cuối: hạn phản hồi đã được chặn trên bởi giờ học nên về lý
                    // thuyết không tới được đây, nhưng dữ liệu cũ (tạo theo luật 24h cố định) vẫn
                    // có thể còn treo. Duyệt một buổi đã qua giờ nghĩa là kích hoạt buổi không ai
                    // vào được — AbandonedSessionJob sẽ auto-complete và trả tiền cho gia sư.
                    var firstStart = booking.ClassSessions
                        .OrderBy(x => x.Scheduledstart)
                        .Select(x => (DateTime?)x.Scheduledstart)
                        .FirstOrDefault();

                    if (firstStart.HasValue && firstStart.Value <= TimeZoneHelper.UtcNow)
                        throw new BookingException(
                            BookingErrorCodes.BookingExpired,
                            "Buổi học đầu tiên đã qua giờ nên không thể chấp nhận booking này. "
                            + "Hệ thống sẽ tự hủy và hoàn cọc cho phụ huynh.",
                            409);

                    // Safety net: bổ sung Remainingamount nếu null (data cũ), KHÔNG ghi đè Depositamount
                    // vì parent đã trả theo số đó rồi.
                    if (booking.Remainingamount == null)
                        booking.Remainingamount = (booking.Finalprice ?? 0) - (booking.Depositamount ?? 0);

                    booking.Status = BookingStatus.DepositPaid;
                    booking.Updatedat = TimeZoneHelper.UtcNow;
                    booking.Responsedeadline = null;

                    // Pay-per-phase: cọc chỉ trả cho buổi ĐẦU → chỉ kích hoạt buổi đầu.
                    // Các buổi 2..N giữ `reserved` cho tới khi phụ huynh trả phần còn lại
                    // (khi đó PaymentService.ActivateRemainingSessionsAsync sẽ kích hoạt).
                    var firstReserved = booking.ClassSessions
                        .Where(x => x.Status == ClassSessionStatus.Reserved)
                        .OrderBy(x => x.Scheduledstart)
                        .Take(1);
                    foreach (var classSession in firstReserved)
                    {
                        classSession.Status = ClassSessionStatus.Scheduled;
                        classSession.Meetinglink ??= classSession.Classsessionid.ToString();
                    }

                    await bookingRepo.SaveChangesAsync();

                    // Lịch chỉ thực sự khóa từ lúc này — mọi booking khác (đã đóng cọc hoặc chưa)
                    // của CÙNG gia sư trùng khung giờ giờ trở thành "thua cuộc", tự động bị hủy.
                    await CancelOverlappingCompetingBookingsAsync(booking, tutorId);
                }

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        var (counterpartId, counterpartIsStudent) = BookingPayerResolver.Resolve(booking);
        var (parentFeePct, tutorFeePct) = await commissionConfigService.GetFeePercentsAsync();

        if (alreadyAccepted)
        {
            var existingChannelId = await chatService.GetOrCreateChannelAsync(counterpartId!, tutorId, counterpartIsStudent);
            return new TutorDecisionResponse
            {
                Booking = MapToResponse(booking, booking.Student, booking.Tutor, booking.Tutorsubjectgradeprice?.Subject, parentFeePct, tutorFeePct),
                ChannelId = existingChannelId
            };
        }

        var scheduledClassSessions = booking.ClassSessions
            .Where(classSession => classSession.Status == ClassSessionStatus.Scheduled)
            .OrderBy(classSession => classSession.Scheduledstart)
            .ToList();

        var channelId = 0;
        try
        {
            channelId = await chatService.GetOrCreateChannelAsync(counterpartId!, tutorId, counterpartIsStudent);
            await chatService.SendMessageAsync(tutorId, channelId, new ChatMessageCreateRequest
            {
                Content = "✅ Gia sư đã chấp nhận yêu cầu đặt lịch",
                MessageType = ChatMessageType.BookingAccepted,
                Metadata = new { bookingId, status = booking.Status }
            });

            if (scheduledClassSessions.Count > 0)
            {
                await chatService.SendMeetLinksAsync(bookingId, scheduledClassSessions.Select((classSession, index) => new ClassSessionMiniResponse
                {
                    ClassSessionId = classSession.Classsessionid,
                    BookingId = bookingId,
                    SessionIndex = index + 1,
                    ScheduledStart = classSession.Scheduledstart,
                    ScheduledEnd = classSession.Scheduledend,
                    MeetingLink = classSession.Meetinglink ?? "",
                    Status = classSession.Status ?? ClassSessionStatus.Scheduled
                }).ToList());
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send chat message for booking {BookingId}", bookingId);
        }

        try
        {
            await notificationService.CreateNotificationAsync(new NotificationRequest
            {
                Userid = counterpartId!,
                Title = "Gia sư đã chấp nhận lịch học",
                Message = $"Gia sư đã chấp nhận yêu cầu đặt lịch #{bookingId}. Buổi học đã được lên lịch.",
                Type = NotificationType.BookingAccepted,
                Referenceid = bookingId.ToString()
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send accept notification for booking {BookingId}", bookingId);
        }

        return new TutorDecisionResponse
        {
            Booking = MapToResponse(booking, booking.Student, booking.Tutor, booking.Tutorsubjectgradeprice?.Subject, parentFeePct, tutorFeePct),
            ChannelId = channelId
        };
    }

    public async Task<BookingResponse> DeclineBookingAsync(string tutorId, int bookingId, string? reason)
    {
        Booking booking;
        await using var tx = await context.Database.BeginTransactionAsync();
        try
        {
            booking = await bookingRepo.FindWithRelationsForUpdateAsync(bookingId)
                ?? throw new BookingException(BookingErrorCodes.BookingNotFound, "Không tìm thấy booking", 404);

            if (booking.Tutorid != tutorId)
                throw new BookingException(BookingErrorCodes.NotBookingOwner, "Bạn không phải gia sư của booking này", 403);

            if (booking.Status != BookingStatus.PendingTutor)
                throw new BookingException(BookingErrorCodes.InvalidBookingStatus, $"Không thể từ chối booking ở trạng thái '{booking.Status}'", 400);

            booking.Status = BookingStatus.Cancelled;
            booking.Cancellationreason = reason;
            booking.Cancelledby = tutorId;
            booking.Cancelledat = TimeZoneHelper.UtcNow;
            booking.Updatedat = TimeZoneHelper.UtcNow;
            booking.Responsedeadline = null;

            foreach (var classSession in booking.ClassSessions.Where(l => l.Status == ClassSessionStatus.Reserved))
                classSession.Status = ClassSessionStatus.Cancelled;

            await RefundPaidBookingAsync(booking, "Hoàn tiền do gia sư từ chối booking");
            // Return the promotion usage consumed at booking creation
            await MV.ApplicationLayer.Helpers.PromotionUsageHelper.ReturnUsageAsync(context, booking.Promotionid);
            await bookingRepo.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        var (declineCounterpartId, declineCounterpartIsStudent) = BookingPayerResolver.Resolve(booking);
        try
        {
            var channelId = await chatService.GetOrCreateChannelAsync(declineCounterpartId!, tutorId, declineCounterpartIsStudent);
            await chatService.SendMessageAsync(tutorId, channelId, new ChatMessageCreateRequest
            {
                Content = string.IsNullOrWhiteSpace(reason)
                    ? "❌ Gia sư đã từ chối yêu cầu đặt lịch"
                    : $"❌ Gia sư đã từ chối yêu cầu đặt lịch. Lý do: {reason}",
                MessageType = ChatMessageType.BookingDeclined,
                Metadata = new { bookingId, status = booking.Status, reason }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send decline chat message for booking {BookingId}", bookingId);
        }

        try
        {
            await notificationService.CreateNotificationAsync(new NotificationRequest
            {
                Userid = ResolveRefundRecipientId(booking)!,
                Title = "Gia sư đã từ chối lịch học",
                Message = $"Gia sư đã từ chối yêu cầu đặt lịch #{bookingId}. Tiền cọc đã được hoàn vào ví của bạn.",
                Type = NotificationType.BookingDeclined,
                Referenceid = bookingId.ToString()
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send decline notification for booking {BookingId}", bookingId);
        }

        var (parentFeePct, tutorFeePct) = await commissionConfigService.GetFeePercentsAsync();
        return MapToResponse(booking, booking.Student, booking.Tutor, booking.Tutorsubjectgradeprice?.Subject, parentFeePct, tutorFeePct);
    }

    /// <summary>
    /// Tìm mọi booking KHÁC của cùng gia sư đang cạnh tranh (pending_tutor/pending_payment — xem
    /// <see cref="BookingScheduleLockPolicy"/>) mà có buổi trùng khung giờ với <paramref
    /// name="acceptedBooking"/> vừa được accept, rồi hủy toàn bộ (không hủy từng buổi lẻ — PHHS
    /// không thể học dở dang 1 khóa mất vài buổi giữa chừng). Hoàn cọc nếu đã đóng, không hoàn nếu
    /// booking chưa từng thanh toán gì. PHẢI gọi bên trong transaction đã lock acceptedBooking.
    /// </summary>
    private async Task CancelOverlappingCompetingBookingsAsync(Booking acceptedBooking, string tutorId)
    {
        var acceptedRanges = acceptedBooking.ClassSessions
            .Where(cs => cs.Status != ClassSessionStatus.Cancelled)
            .Select(cs => (Start: cs.Scheduledstart, End: cs.Scheduledend))
            .ToList();
        if (acceptedRanges.Count == 0) return;

        var rangeMin = acceptedRanges.Min(r => r.Start);
        var rangeMax = acceptedRanges.Max(r => r.End);

        // Pre-filter thô bằng khoảng thời gian tổng của các buổi vừa accept, rồi so khớp chính xác
        // từng buổi trong bộ nhớ — tránh dịch LINQ phức tạp (list.Any(...)) sang SQL không chắc ăn.
        var candidateSessions = await context.ClassSessions
            .Where(cs => cs.Tutorid == tutorId
                && cs.Bookingid != acceptedBooking.Bookingid
                && cs.Status != ClassSessionStatus.Cancelled
                && cs.Status != ClassSessionStatus.CancelledNoshow
                && cs.Scheduledstart < rangeMax
                && cs.Scheduledend > rangeMin)
            .Select(cs => new { cs.Bookingid, cs.Scheduledstart, cs.Scheduledend, BookingStatus = cs.Booking!.Status })
            .ToListAsync();

        var competingBookingIds = candidateSessions
            .Where(cs => BookingScheduleLockPolicy.IsCompetingStatus(cs.BookingStatus)
                && acceptedRanges.Any(r => cs.Scheduledstart < r.End && cs.Scheduledend > r.Start))
            .Select(cs => cs.Bookingid!.Value)
            .Distinct()
            .ToList();

        foreach (var competingId in competingBookingIds)
        {
            var competing = await bookingRepo.FindWithRelationsForUpdateAsync(competingId);
            if (competing == null) continue;
            // Re-check dưới lock — có thể vừa bị xử lý bởi luồng khác giữa lúc query và lúc lock.
            if (!BookingScheduleLockPolicy.IsCompetingStatus(competing.Status)) continue;

            var wasPaid = competing.Depositpaidat != null;
            competing.Status = BookingStatus.Cancelled;
            competing.Cancellationreason = "Tự động hủy: gia sư đã xác nhận một yêu cầu khác trùng khung giờ.";
            competing.Cancelledby = tutorId;
            competing.Cancelledat = TimeZoneHelper.UtcNow;
            competing.Updatedat = TimeZoneHelper.UtcNow;
            competing.Responsedeadline = null;

            foreach (var cs in competing.ClassSessions.Where(x => x.Status == ClassSessionStatus.Reserved))
                cs.Status = ClassSessionStatus.Cancelled;

            if (wasPaid)
                await RefundPaidBookingAsync(competing, "Hoàn tiền do gia sư xác nhận yêu cầu khác trùng khung giờ");
            await MV.ApplicationLayer.Helpers.PromotionUsageHelper.ReturnUsageAsync(context, competing.Promotionid);
            await bookingRepo.SaveChangesAsync();

            try
            {
                await notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = ResolveRefundRecipientId(competing)!,
                    Title = "Yêu cầu đặt lịch đã bị hủy",
                    Message = wasPaid
                        ? $"Rất tiếc, khung giờ bạn chọn đã được gia sư xác nhận cho một yêu cầu khác. Booking #{competing.Bookingid} đã bị hủy, tiền cọc đã được hoàn về ví của bạn."
                        : $"Rất tiếc, khung giờ bạn chọn đã được gia sư xác nhận cho một yêu cầu khác. Booking #{competing.Bookingid} đã bị hủy.",
                    Type = NotificationType.BookingDeclined,
                    Referenceid = competing.Bookingid.ToString()
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send auto-cancel notification for booking {BookingId}", competing.Bookingid);
            }
        }
    }

    private async Task RefundPaidBookingAsync(Booking booking, string description)
    {
        if (booking.Depositpaidat == null)
            throw new InvalidOperationException(
                $"Booking #{booking.Bookingid} is pending tutor response without a paid deposit.");

        var refundAmount = TutorResponseTimeoutPolicy.ParentRefundAmount(booking);

        // Hoàn về người đã trả: phụ huynh đặt hộ → Parentid; học sinh tự đặt → ví học sinh.
        var refundRecipientId = ResolveRefundRecipientId(booking);

        if (refundAmount <= 0 || string.IsNullOrWhiteSpace(refundRecipientId))
            throw new InvalidOperationException(
                $"Booking #{booking.Bookingid} has no valid refund recipient or amount.");

        var now = TimeZoneHelper.UtcNow;
        var parentWallet = await WalletLockHelper.GetOrCreateForUpdateAsync(context, refundRecipientId, now);
        parentWallet.Balance = (parentWallet.Balance ?? 0) + refundAmount;
        parentWallet.Lastupdated = now;

        context.Wallettransactions.Add(new Wallettransaction
        {
            Wallet = parentWallet,
            Amount = refundAmount,
            Transactiontype = TransactionType.Refund,
            Referencetable = ReferenceTable.Booking,
            Referenceid = booking.Bookingid,
            Description = description,
            Createdat = now
        });

        var tutorEscrowAmount = TutorResponseTimeoutPolicy.TutorEscrowAmount(booking);

        if (tutorEscrowAmount > 0)
        {
            if (string.IsNullOrWhiteSpace(booking.Tutorid))
                throw new InvalidOperationException($"Booking #{booking.Bookingid} has escrow but no tutor id.");

            var tutorWallet = await WalletLockHelper.GetRequiredForUpdateAsync(context, booking.Tutorid);
            if ((tutorWallet.Frozenbalance ?? 0) < tutorEscrowAmount)
                throw new InvalidOperationException(
                    $"Tutor escrow balance is insufficient for booking #{booking.Bookingid}.");

            tutorWallet.Frozenbalance = (tutorWallet.Frozenbalance ?? 0) - tutorEscrowAmount;
            tutorWallet.Lastupdated = now;

            context.Wallettransactions.Add(new Wallettransaction
            {
                Wallet = tutorWallet,
                Amount = -tutorEscrowAmount,
                Transactiontype = TransactionType.EscrowReversal,
                Referencetable = ReferenceTable.Booking,
                Referenceid = booking.Bookingid,
                Description = $"{description} - release escrow",
                Createdat = TimeZoneHelper.UtcNow
            });
        }

        booking.Refundamount = refundAmount;
        booking.Refundstatus = RefundStatus.Refunded;
        booking.Escrowstatus = EscrowStatus.Refunded;
    }

    /// <summary>
    /// Bổ sung Remainingamount nếu còn null — không overwrite Depositamount đã được confirm thanh toán.
    /// </summary>
    private static void EnsureRemainingAmountCalculated(Booking booking)
    {
        if (booking.Remainingamount != null) return;

        var finalPrice = booking.Finalprice ?? 0;
        var deposit = booking.Depositamount ?? 0;

        // Depositamount chưa có — tính lại cả hai từ đầu
        if (deposit == 0)
        {
            var sessions = booking.Totalsessions ?? 1;
            var (d, r) = BookingFeeCalculator.CalculatePaymentPhases(finalPrice, sessions);
            booking.Depositamount = d;
            booking.Remainingamount = r;
        }
        else
        {
            booking.Remainingamount = finalPrice - deposit;
        }
    }
}
