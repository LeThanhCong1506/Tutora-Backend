using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Interfaces;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.BackgroundJobs;

public class GhostUserCleanupJob(IServiceProvider sp, ILogger<GhostUserCleanupJob> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromHours(12);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("GhostUserCleanupJob bắt đầu chạy.");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CleanupGhostUsersAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Lỗi khi chạy dọn dẹp các tài khoản rác (Ghost Users).");
            }

            // Chạy lặp lại mỗi 12 tiếng
            await Task.Delay(_interval, ct);
        }
    }

    private async Task CleanupGhostUsersAsync(CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        
        // Quét những User chưa verify email và đã tạo quá 12h
        var thresholdTime = TimeZoneHelper.UtcNow.AddHours(-12);

        var ghostUsers = await db.Users
            .Include(u => u.Wallet)
            .Include(u => u.Tutorprofile)
            .Include(u => u.StudentprofileLinkedusers)
            .Where(u => u.Isemailverified == false 
                     && u.Createdat != null 
                     && u.Createdat < thresholdTime
                     && !u.StudentprofileLinkedusers.Any(s => s.Bookings.Any()) // Không xóa nếu học viên đã có Booking
                     && (u.Tutorprofile == null || !u.Tutorprofile.Bookings.Any())) // Không xóa nếu gia sư đã có Booking
            .ToListAsync(ct);

        if (ghostUsers.Count == 0)
        {
            return;
        }

        logger.LogInformation("Phát hiện {Count} tài khoản rác (chưa xác thực quá 12h). Tiến hành xóa vĩnh viễn...", ghostUsers.Count);

        foreach (var user in ghostUsers)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                // Tự động gỡ các profile con trước khi xóa User để tránh lỗi Khóa ngoại (Foreign Key Constraint)
                if (user.Wallet != null)
                {
                    db.Wallets.Remove(user.Wallet);
                }

                if (user.Tutorprofile != null)
                {
                    db.Tutorprofiles.Remove(user.Tutorprofile);
                }

                if (user.StudentprofileLinkedusers.Any())
                {
                    db.Studentprofiles.RemoveRange(user.StudentprofileLinkedusers);
                }

                // Xóa User
                db.Users.Remove(user);
                
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                logger.LogError(ex, "Không thể xóa tài khoản rác {UserId}.", user.Userid);
            }
        }

        logger.LogInformation("Hoàn tất dọn dẹp tài khoản rác.");
    }
}
