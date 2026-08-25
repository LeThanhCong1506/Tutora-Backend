using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel.Admin;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services;

public class WithdrawalLimitService(IAppDbContext context) : IWithdrawalLimitService
{
    private const decimal Fallback = 10000m;

    public async Task<decimal> GetMinWithdrawalAmountAsync(CancellationToken ct = default)
    {
        var value = await context.Systemconfigs
            .AsNoTracking()
            .Where(c => c.Configkey == WithdrawalLimitConfigKeys.MinWithdrawalAmount)
            .Select(c => c.Configvalue)
            .FirstOrDefaultAsync(ct);

        return decimal.TryParse(value, out var parsed) ? parsed : Fallback;
    }

    public async Task<AdminWithdrawalLimitResponse> AdminGetAsync(CancellationToken ct = default)
    {
        var row = await context.Systemconfigs
            .AsNoTracking()
            .Where(c => c.Configkey == WithdrawalLimitConfigKeys.MinWithdrawalAmount)
            .Select(c => new { c.Configvalue, c.Updatedat, UpdatedByName = c.UpdatedbyNavigation!.Fullname })
            .FirstOrDefaultAsync(ct);

        return new AdminWithdrawalLimitResponse
        {
            MinWithdrawalAmount = decimal.TryParse(row?.Configvalue, out var parsed) ? parsed : Fallback,
            UpdatedAt = row?.Updatedat,
            UpdatedByName = row?.UpdatedByName,
        };
    }

    public async Task<AdminWithdrawalLimitResponse> AdminSetAsync(
        AdminSetWithdrawalLimitRequest request, string? updatedByUserId, CancellationToken ct = default)
    {
        if (request.MinWithdrawalAmount < 0)
        {
            throw new BookingException(
                "INVALID_WITHDRAWAL_LIMIT", "Số tiền rút tối thiểu không được âm.", 400);
        }

        var cfg = await context.Systemconfigs
            .FirstOrDefaultAsync(c => c.Configkey == WithdrawalLimitConfigKeys.MinWithdrawalAmount, ct);
        if (cfg is null)
        {
            cfg = new Systemconfig
            {
                Configkey = WithdrawalLimitConfigKeys.MinWithdrawalAmount,
                Description = "Số tiền tối thiểu (VND) gia sư/user phải đạt trước khi tạo yêu cầu rút tiền.",
            };
            context.Systemconfigs.Add(cfg);
        }
        cfg.Configvalue = request.MinWithdrawalAmount.ToString("0.##");
        cfg.Updatedby = updatedByUserId;
        cfg.Updatedat = TimeZoneHelper.UtcNow;

        await context.SaveChangesAsync(ct);

        return await AdminGetAsync(ct);
    }
}
