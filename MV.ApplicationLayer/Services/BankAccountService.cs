using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services;

public class BankAccountService(
    IAppDbContext context,
    IBankAccountOtpService otpService,
    INotificationService notificationService,
    ILogger<BankAccountService> logger) : IBankAccountService
{
    public async Task<BankAccountResponse> GetAsync(string userId, CancellationToken ct = default)
    {
        var bankAccount = await context.BankAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Userid == userId, ct);

        return MapToResponse(bankAccount);
    }

    public async Task SendOtpAsync(string userId, CancellationToken ct = default)
    {
        var user = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Userid == userId, ct)
            ?? throw new UserNotFoundException(userId);

        if (string.IsNullOrWhiteSpace(user.Phone) || user.Isphoneverified != true)
            throw new BankAccountOtpPhoneNotVerifiedException();

        if (await IsParentManagedStudentAsync(userId, ct))
            throw new BankAccountNotEligibleException();

        await otpService.SendAsync(userId, user.Phone!);

        logger.LogInformation("Sent bank-account OTP for user {UserId}", userId);
    }

    public Task VerifyOtpAsync(string userId, string code, CancellationToken ct = default)
        => otpService.VerifyAsync(userId, code);

    public async Task<BankAccountResponse> SaveAsync(
        string userId,
        SaveBankAccountRequest request,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        if (!await otpService.IsApprovedAsync(userId))
            throw new BankAccountOtpRequiredException();

        var userExists = await context.Users.AsNoTracking().AnyAsync(u => u.Userid == userId, ct);
        if (!userExists)
            throw new UserNotFoundException(userId);

        await using var transaction = await context.Database.BeginTransactionAsync(ct);

        var now = TimeZoneHelper.UtcNow;
        var bankAccount = await context.BankAccounts.FirstOrDefaultAsync(b => b.Userid == userId, ct);
        var isNew = bankAccount == null;

        var oldBankName = bankAccount?.Bankname;
        var oldAccountNumber = bankAccount?.Accountnumber;
        var oldAccountHolderName = bankAccount?.Accountholdername;

        if (bankAccount == null)
        {
            bankAccount = new BankAccount { Userid = userId, Createdat = now };
            context.BankAccounts.Add(bankAccount);
        }

        bankAccount.Bankname = request.BankName.Trim();
        bankAccount.Accountnumber = request.AccountNumber.Trim();
        bankAccount.Accountholdername = request.AccountHolderName.Trim();
        bankAccount.Updatedat = now;

        // Save trước để có Bankaccountid thật (identity column) cho case tạo mới, trước khi
        // dòng audit log tham chiếu tới nó.
        await context.SaveChangesAsync(ct);

        context.BankAccountAuditLogs.Add(new BankAccountAuditLog
        {
            Userid = userId,
            Bankaccountid = bankAccount.Bankaccountid,
            Action = isNew ? BankAccountAuditAction.Created : BankAccountAuditAction.Updated,
            Oldbankname = oldBankName,
            Oldaccountnumber = oldAccountNumber,
            Oldaccountholdername = oldAccountHolderName,
            Newbankname = bankAccount.Bankname,
            Newaccountnumber = bankAccount.Accountnumber,
            Newaccountholdername = bankAccount.Accountholdername,
            Changedat = now,
            Ipaddress = ipAddress,
            Useragent = userAgent
        });
        await context.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);

        // Cờ approved dùng 1 lần rồi bỏ — không consume tường minh thì user có thể lưu/xoá liên
        // tiếp trong TTL của cờ mà không cần OTP mới, vi phạm yêu cầu "mọi lần lưu đều cần OTP".
        await otpService.ConsumeApprovalAsync(userId);

        await SendTripwireNotificationAsync(userId, deleted: false, ct);

        logger.LogInformation(
            "{Action} bank account for user {UserId}", isNew ? "Created" : "Updated", userId);

        return MapToResponse(bankAccount);
    }

    public async Task DeleteAsync(string userId, string? ipAddress, string? userAgent, CancellationToken ct = default)
    {
        if (!await otpService.IsApprovedAsync(userId))
            throw new BankAccountOtpRequiredException();

        var bankAccount = await context.BankAccounts.FirstOrDefaultAsync(b => b.Userid == userId, ct);
        if (bankAccount == null)
        {
            // Không có gì để xoá — vẫn tiêu thụ cờ approved để tránh nó bị dùng lại cho một
            // request khác trong lúc còn hiệu lực.
            await otpService.ConsumeApprovalAsync(userId);
            return;
        }

        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var now = TimeZoneHelper.UtcNow;

        // Ghi audit log TRƯỚC khi xoá — bank_account_id phải trỏ tới một dòng bank_accounts còn
        // tồn tại lúc INSERT (ràng buộc FK), sau đó DELETE bank_accounts sẽ tự kích hoạt
        // "ON DELETE SET NULL" để null hoá cột này trên dòng log vừa ghi.
        context.BankAccountAuditLogs.Add(new BankAccountAuditLog
        {
            Userid = userId,
            Bankaccountid = bankAccount.Bankaccountid,
            Action = BankAccountAuditAction.Deleted,
            Oldbankname = bankAccount.Bankname,
            Oldaccountnumber = bankAccount.Accountnumber,
            Oldaccountholdername = bankAccount.Accountholdername,
            Changedat = now,
            Ipaddress = ipAddress,
            Useragent = userAgent
        });
        await context.SaveChangesAsync(ct);

        context.BankAccounts.Remove(bankAccount);
        await context.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);

        await otpService.ConsumeApprovalAsync(userId);

        await SendTripwireNotificationAsync(userId, deleted: true, ct);

        logger.LogInformation("Deleted bank account for user {UserId}", userId);
    }

    public async Task<List<BankAccountAuditLogResponse>> GetHistoryAsync(string userId, CancellationToken ct = default)
    {
        return await context.BankAccountAuditLogs
            .AsNoTracking()
            .Where(l => l.Userid == userId)
            .OrderByDescending(l => l.Changedat)
            .Select(l => new BankAccountAuditLogResponse
            {
                Action = l.Action,
                OldBankName = l.Oldbankname,
                OldAccountNumber = l.Oldaccountnumber,
                OldAccountHolderName = l.Oldaccountholdername,
                NewBankName = l.Newbankname,
                NewAccountNumber = l.Newaccountnumber,
                NewAccountHolderName = l.Newaccountholdername,
                ChangedAt = l.Changedat
            })
            .ToListAsync(ct);
    }

    /// <summary>
    /// Cùng định nghĩa với SimpleAuthService/RefreshTokenService.IsParentManagedChildAccountAsync,
    /// lặp lại độc lập ở đây theo đúng convention hiện có của codebase (mỗi service tự kiểm tra
    /// cổng này, không tách helper dùng chung).
    /// </summary>
    private async Task<bool> IsParentManagedStudentAsync(string userId, CancellationToken ct)
    {
        var profile = await context.Studentprofiles
            .AsNoTracking()
            .FirstOrDefaultAsync(sp => (sp.Studentid == userId || sp.Linkeduserid == userId) && sp.Deletedat == null, ct);
        return profile?.Parentid != null;
    }

    private async Task SendTripwireNotificationAsync(string userId, bool deleted, CancellationToken ct)
    {
        try
        {
            await notificationService.CreateNotificationAsync(new NotificationRequest
            {
                Userid = userId,
                Title = deleted ? "Tài khoản ngân hàng đã bị xoá" : "Tài khoản ngân hàng đã được cập nhật",
                Message = deleted
                    ? "Tài khoản ngân hàng của bạn vừa được xoá. Nếu không phải bạn, vui lòng liên hệ hỗ trợ ngay."
                    : "Tài khoản ngân hàng của bạn vừa được cập nhật. Nếu không phải bạn, vui lòng liên hệ hỗ trợ ngay.",
                Type = deleted ? NotificationType.BankAccountDeleted : NotificationType.BankAccountUpdated
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send bank-account tripwire notification for user {UserId}", userId);
        }
    }

    private static BankAccountResponse MapToResponse(BankAccount? bankAccount) => new()
    {
        BankName = bankAccount?.Bankname,
        AccountNumber = bankAccount?.Accountnumber,
        AccountHolderName = bankAccount?.Accountholdername,
        BankChangedAt = bankAccount?.Updatedat
    };
}
