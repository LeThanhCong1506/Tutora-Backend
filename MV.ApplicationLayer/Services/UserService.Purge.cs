using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services
{
    /// <summary>
    /// Permanent erasure of an account and everything attached to it.
    /// </summary>
    /// <remarks>
    /// Distinct from <c>DeleteUserAsync</c>, which only flags <c>is_deleted</c> and leaves every row
    /// in place. This one cannot be undone and the product has no restore anywhere, so it is gated
    /// three ways: the account must already be offline, nothing financial may still be outstanding,
    /// and the operator has to type a sentence naming both themselves and the target.
    ///
    /// Deleting rows explicitly rather than leaning on the database cascades is deliberate. The
    /// cascade from <c>users</c> reaches <c>tutorprofiles</c> and from there other people's
    /// bookings, while nineteen foreign keys point at <c>users</c> with NO ACTION/RESTRICT and would
    /// abort the statement part-way. Doing it in dependency order inside one transaction is the only
    /// way the outcome is predictable.
    /// </remarks>
    public partial class UserService
    {
        /// <summary>Bookings that still owe somebody a session or a settlement.</summary>
        private static readonly string[] UnsettledBookingStatuses =
        {
            BookingStatus.PendingTutor, BookingStatus.Accepted, BookingStatus.PendingPayment,
            BookingStatus.Paid, BookingStatus.Ongoing, BookingStatus.DepositPaid,
            BookingStatus.PendingRemainingPayment
        };

        public async Task<UserPurgePreflightResponse> GetPurgePreflightAsync(string userId, string adminUserId)
        {
            var user = await _userRepository.GetUserByIdAsync(userId)
                ?? throw new UserNotFoundException(userId);
            var admin = await _userRepository.GetUserByIdAsync(adminUserId);

            var response = new UserPurgePreflightResponse
            {
                UserId = user.Userid,
                FullName = user.Fullname,
                Role = user.Primaryrole,
                ConfirmationPhrase = UserPurgeConfirmation.Build(admin?.Fullname, user.Fullname),
                Blockers = await CollectPurgeBlockersAsync(user.Userid, user.Status),
                Footprint = await CountFootprintAsync(user.Userid)
            };

            response.CanPurge = response.Blockers.Count == 0;
            return response;
        }

        public async Task<UserPurgeResultResponse> AdminPurgeUserAsync(
            string userId, string confirmationPhrase, string adminUserId)
        {
            var user = await _userRepository.GetUserByIdAsync(userId)
                ?? throw new UserNotFoundException(userId);
            var admin = await _userRepository.GetUserByIdAsync(adminUserId);

            // Re-derive and re-check the sentence here. A client that skipped the dialog, or typed it
            // against a different row, must not be able to reach the delete.
            var expected = UserPurgeConfirmation.Build(admin?.Fullname, user.Fullname);
            if (!UserPurgeConfirmation.Matches(expected, confirmationPhrase))
                throw new InvalidOperationException(
                    $"Câu xác nhận không khớp. Vui lòng nhập chính xác: \"{expected}\"");

            var blockers = await CollectPurgeBlockersAsync(user.Userid, user.Status);
            if (blockers.Count > 0)
                throw new InvalidOperationException(
                    "Không thể xóa vĩnh viễn tài khoản này. " + string.Join(" ", blockers));

            // Counted before the delete — afterwards there is nothing left to count.
            var footprint = await CountFootprintAsync(user.Userid);
            var fullName = user.Fullname;

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                await PurgeRowsAsync(user.Userid);
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

            // The account is gone, so this log line is the only remaining record that it existed.
            _logger.LogWarning(
                "Admin {AdminId} permanently erased user {UserId} ({FullName}): {Bookings} booking(s), {Sessions} session(s), {Transactions} wallet transaction(s), {Feedbacks} feedback(s), {Messages} chat message(s).",
                adminUserId, userId, fullName, footprint.Bookings, footprint.ClassSessions,
                footprint.WalletTransactions, footprint.Feedbacks, footprint.ChatMessages);

            return new UserPurgeResultResponse
            {
                UserId = userId,
                FullName = fullName,
                Deleted = footprint,
                PurgedAt = TimeZoneHelper.UtcNow,
                PurgedByName = admin?.Fullname
            };
        }

        // ─── Guards ───────────────────────────────────────────────────────────

        private async Task<List<string>> CollectPurgeBlockersAsync(string userId, int? status)
        {
            var blockers = new List<string>();

            // Erasing an account that can still sign in would drop somebody mid-session, and skips
            // the step where an operator has to make the suspend/block call first.
            if (status != 0)
                blockers.Add("Tài khoản vẫn đang hoạt động — hãy chặn tài khoản trước khi xóa vĩnh viễn.");

            var wallet = await _context.Wallets.AsNoTracking()
                .Where(w => w.Userid == userId)
                .Select(w => new { Balance = w.Balance ?? 0, Frozen = w.Frozenbalance ?? 0 })
                .FirstOrDefaultAsync();

            if (wallet != null && wallet.Balance > 0)
                blockers.Add($"Ví còn {wallet.Balance:N0}đ khả dụng — cần rút hoặc chuyển hết trước khi xóa.");
            if (wallet != null && wallet.Frozen > 0)
                blockers.Add($"Ví còn {wallet.Frozen:N0}đ đang ký quỹ cho các buổi chưa tất toán.");

            var liveBookings = await _context.Bookings.AsNoTracking()
                .CountAsync(b => (b.Parentid == userId || b.Tutorid == userId)
                                 && UnsettledBookingStatuses.Contains(b.Status!));
            if (liveBookings > 0)
                blockers.Add($"Còn {liveBookings} khóa học chưa tất toán — cần hoàn tất hoặc hủy trước.");

            var pendingWithdrawals = await _context.Withdrawalrequests.AsNoTracking()
                .CountAsync(w => w.Userid == userId
                                 && (w.Status == WithdrawalStatus.Pending
                                     || w.Status == WithdrawalStatus.PendingReview
                                     || w.Status == WithdrawalStatus.Approved));
            if (pendingWithdrawals > 0)
                blockers.Add($"Còn {pendingWithdrawals} yêu cầu rút tiền đang chờ xử lý.");

            var openDisputes = await _context.Disputes.AsNoTracking()
                .CountAsync(d => d.Createdby == userId
                                 && d.Status != DisputeStatus.Resolved
                                 && d.Status != DisputeStatus.Closed);
            if (openDisputes > 0)
                blockers.Add($"Còn {openDisputes} khiếu nại chưa đóng.");

            // admin_wallet_transfers.created_by and system_fund_topups.created_by are NOT NULL, so
            // the actor cannot be nulled out of them and the delete would abort mid-transaction.
            // Only ever non-zero for an Admin/Staff account.
            var transfersCreated = await _context.AdminWalletTransfers.AsNoTracking()
                .CountAsync(t => t.Createdby == userId);
            if (transfersCreated > 0)
                blockers.Add($"Tài khoản đã thực hiện {transfersCreated} giao dịch chuyển ví với tư cách quản trị — hồ sơ kế toán không cho phép xóa người thực hiện.");

            var fundTopups = await _context.SystemFundTopups.AsNoTracking()
                .CountAsync(t => t.Createdby == userId);
            if (fundTopups > 0)
                blockers.Add($"Tài khoản đã thực hiện {fundTopups} lần nạp quỹ hệ thống — hồ sơ kế toán không cho phép xóa người thực hiện.");

            return blockers;
        }

        private async Task<UserPurgeFootprint> CountFootprintAsync(string userId)
        {
            var bookingIds = await BookingScopeQuery(userId).ToListAsync();

            return new UserPurgeFootprint
            {
                Bookings = bookingIds.Count,
                ClassSessions = await _context.ClassSessions.AsNoTracking()
                    .CountAsync(s => s.Bookingid.HasValue && bookingIds.Contains(s.Bookingid.Value)),
                WalletTransactions = await _context.Wallettransactions.AsNoTracking()
                    .CountAsync(t => _context.Wallets.Any(w => w.Userid == userId && w.Walletid == t.Walletid)),
                Feedbacks = await _context.Feedbacks.AsNoTracking()
                    .CountAsync(f => f.Fromuserid == userId || f.Touserid == userId),
                Disputes = await _context.Disputes.AsNoTracking()
                    .CountAsync(d => d.Createdby == userId),
                ChatMessages = await _context.Chatmessages.AsNoTracking()
                    .CountAsync(m => m.Senderid == userId),
                Warnings = await _context.Userwarnings.AsNoTracking()
                    .CountAsync(w => w.Userid == userId)
            };
        }

        /// <summary>
        /// Every booking the account is a party to — as the payer, the tutor, or the student behind
        /// one of their child profiles. Child profiles cascade away with their parent, so their
        /// bookings have to go in the same sweep.
        /// </summary>
        private IQueryable<int> BookingScopeQuery(string userId)
        {
            var studentIds = _context.Studentprofiles.AsNoTracking()
                .Where(p => p.Linkeduserid == userId || p.Studentid == userId || p.Parentid == userId)
                .Select(p => p.Studentid);

            return _context.Bookings.AsNoTracking()
                .Where(b => b.Parentid == userId
                            || b.Tutorid == userId
                            || (b.Studentid != null && studentIds.Contains(b.Studentid)))
                .Select(b => b.Bookingid);
        }

        // ─── The erase itself ─────────────────────────────────────────────────

        /// <summary>
        /// Removes every row keyed to the account, children first.
        /// </summary>
        /// <remarks>
        /// Raw SQL because this is set-based deletion across ~20 tables; loading them into the change
        /// tracker to delete them would be slower and no clearer. Order matters and is dictated by
        /// the live schema: rows that block <c>bookings</c>/<c>class_sessions</c>/<c>wallets</c> go
        /// first, then those parents, then the audit columns that outlive the person, then the user.
        /// Anything missed surfaces as a foreign-key violation that rolls the whole thing back, which
        /// is the safe direction to fail in.
        /// </remarks>
        private async Task PurgeRowsAsync(string userId)
        {
            var db = _context.Database;

            // The set of bookings this account is party to, resolved once and reused. Kept as SQL so
            // every statement below sees the same set as it shrinks.
            const string BookingScope = @"
                SELECT b.booking_id FROM bookings b
                WHERE b.parent_id = {0} OR b.tutor_id = {0}
                   OR b.student_id IN (SELECT sp.student_id FROM student_profiles sp
                                       WHERE sp.linked_user_id = {0} OR sp.student_id = {0} OR sp.parent_id = {0})";

            const string SessionScope = @"
                SELECT cs.class_session_id FROM class_sessions cs WHERE cs.booking_id IN (" + BookingScope + ")";

            // 1. A continuation session points at the session it continues; clear the self-reference
            //    before the rows start disappearing underneath each other.
            await db.ExecuteSqlRawAsync(
                "UPDATE class_sessions SET original_session_id = NULL WHERE original_session_id IN (" + SessionScope + ")",
                userId);

            // 2. Rows that block bookings and class_sessions.
            await db.ExecuteSqlRawAsync(
                "DELETE FROM feedbacks WHERE from_user_id = {0} OR to_user_id = {0}"
                + " OR booking_id IN (" + BookingScope + ")"
                + " OR class_session_id IN (" + SessionScope + ")",
                userId);

            await db.ExecuteSqlRawAsync("UPDATE disputes SET resolved_by = NULL WHERE resolved_by = {0}", userId);
            await db.ExecuteSqlRawAsync("DELETE FROM dispute_messages WHERE sender_id = {0}", userId);
            await db.ExecuteSqlRawAsync("DELETE FROM dispute_evidences WHERE uploaded_by = {0}", userId);
            await db.ExecuteSqlRawAsync(
                "DELETE FROM disputes WHERE created_by = {0}"
                + " OR booking_id IN (" + BookingScope + ")"
                + " OR class_session_id IN (" + SessionScope + ")",
                userId);

            await db.ExecuteSqlRawAsync(
                "DELETE FROM payment_requests WHERE booking_id IN (" + BookingScope + ")", userId);
            await db.ExecuteSqlRawAsync(
                "DELETE FROM topup_requests WHERE booking_id IN (" + BookingScope + ")", userId);

            // Someone else's warning may cite one of these bookings as evidence. Keep their warning,
            // drop the dangling pointer.
            await db.ExecuteSqlRawAsync(
                "UPDATE user_warnings SET related_booking_id = NULL WHERE related_booking_id IN (" + BookingScope + ")",
                userId);

            // 3. Money. The pre-flight has already established the wallet is empty.
            await db.ExecuteSqlRawAsync(
                "DELETE FROM withdrawal_requests WHERE user_id = {0}"
                + " OR wallet_id IN (SELECT wallet_id FROM wallets WHERE user_id = {0})", userId);
            await db.ExecuteSqlRawAsync(
                "DELETE FROM wallet_transactions WHERE wallet_id IN (SELECT wallet_id FROM wallets WHERE user_id = {0})",
                userId);
            await db.ExecuteSqlRawAsync("DELETE FROM wallets WHERE user_id = {0}", userId);
            await db.ExecuteSqlRawAsync(
                "DELETE FROM admin_wallet_transfers WHERE recipient_user_id = {0}", userId);

            // 4. The bookings themselves; class_sessions cascade from here.
            await db.ExecuteSqlRawAsync(
                "DELETE FROM bookings WHERE booking_id IN (" + BookingScope + ")", userId);

            // 5. Records that belong to the platform rather than the person: keep the row, forget who.
            await db.ExecuteSqlRawAsync("UPDATE user_warnings SET issued_by = NULL WHERE issued_by = {0}", userId);
            await db.ExecuteSqlRawAsync("UPDATE profile_suspensions SET created_by = NULL WHERE created_by = {0}", userId);
            await db.ExecuteSqlRawAsync("UPDATE system_configs SET updated_by = NULL WHERE updated_by = {0}", userId);
            await db.ExecuteSqlRawAsync("UPDATE commission_config_history SET changed_by = NULL WHERE changed_by = {0}", userId);
            await db.ExecuteSqlRawAsync("UPDATE learning_materials SET uploaded_by = NULL WHERE uploaded_by = {0}", userId);
            await db.ExecuteSqlRawAsync("UPDATE class_sessions SET interrupted_by = NULL WHERE interrupted_by = {0}", userId);

            // 6. Their own conversations.
            await db.ExecuteSqlRawAsync("DELETE FROM chat_messages WHERE sender_id = {0}", userId);

            // 7. The account. The remaining ~34 foreign keys cascade from here — profile, warnings,
            //    suspensions, notifications, sessions, tokens, permissions, AI credits.
            await db.ExecuteSqlRawAsync("DELETE FROM users WHERE user_id = {0}", userId);
        }
    }
}
