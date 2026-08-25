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

public class CommissionConfigService(IAppDbContext context) : ICommissionConfigService
{
    private const int HistoryPageSize = 20;

    public async Task<(decimal ParentPercent, decimal TutorPercent)> GetFeePercentsAsync(CancellationToken ct = default)
    {
        var values = await context.Systemconfigs
            .AsNoTracking()
            .Where(c => c.Configkey == CommissionConfigKeys.ParentFeePercent
                || c.Configkey == CommissionConfigKeys.TutorFeePercent)
            .ToDictionaryAsync(c => c.Configkey, c => c.Configvalue, ct);

        var parent = ParseFractionOrDefault(
            values.GetValueOrDefault(CommissionConfigKeys.ParentFeePercent), 0.05m);
        var tutor = ParseFractionOrDefault(
            values.GetValueOrDefault(CommissionConfigKeys.TutorFeePercent), 0.05m);
        return (parent, tutor);
    }

    public async Task<AdminCommissionConfigResponse> AdminGetAsync(CancellationToken ct = default)
    {
        var rows = await context.Systemconfigs
            .AsNoTracking()
            .Where(c => c.Configkey == CommissionConfigKeys.ParentFeePercent
                || c.Configkey == CommissionConfigKeys.TutorFeePercent)
            .Select(c => new { c.Configkey, c.Configvalue, c.Updatedat, UpdatedByName = c.UpdatedbyNavigation!.Fullname })
            .ToListAsync(ct);

        var parentRow = rows.FirstOrDefault(r => r.Configkey == CommissionConfigKeys.ParentFeePercent);
        var tutorRow = rows.FirstOrDefault(r => r.Configkey == CommissionConfigKeys.TutorFeePercent);
        var latest = rows.Where(r => r.Updatedat != null).OrderByDescending(r => r.Updatedat).FirstOrDefault();

        var history = await context.CommissionConfigHistories
            .AsNoTracking()
            .OrderByDescending(h => h.Changedat)
            .Take(HistoryPageSize)
            .Select(h => new AdminCommissionConfigHistoryItem
            {
                ParentFeePercent = h.Parentfeepercent * 100,
                TutorFeePercent = h.Tutorfeepercent * 100,
                ChangedAt = h.Changedat,
                ChangedByName = h.ChangedbyNavigation!.Fullname,
            })
            .ToListAsync(ct);

        return new AdminCommissionConfigResponse
        {
            ParentFeePercent = ParseFractionOrDefault(parentRow?.Configvalue, 0.05m) * 100,
            TutorFeePercent = ParseFractionOrDefault(tutorRow?.Configvalue, 0.05m) * 100,
            UpdatedAt = latest?.Updatedat,
            UpdatedByName = latest?.UpdatedByName,
            History = history,
        };
    }

    public async Task<AdminCommissionConfigResponse> AdminSetAsync(
        AdminSetCommissionConfigRequest request, string? updatedByUserId, CancellationToken ct = default)
    {
        if (request.ParentFeePercent < 0 || request.ParentFeePercent > 100
            || request.TutorFeePercent < 0 || request.TutorFeePercent > 100)
        {
            throw new BookingException(
                "INVALID_COMMISSION_PERCENT", "Phí sàn phải nằm trong khoảng 0-100%.", 400);
        }

        var parentFraction = request.ParentFeePercent / 100m;
        var tutorFraction = request.TutorFeePercent / 100m;
        var now = TimeZoneHelper.UtcNow;

        await UpsertConfigAsync(CommissionConfigKeys.ParentFeePercent, parentFraction,
            "Phí sàn cộng thêm vào giá cho phụ huynh (dạng phân số, 0.05 = 5%).", updatedByUserId, now, ct);
        await UpsertConfigAsync(CommissionConfigKeys.TutorFeePercent, tutorFraction,
            "Phí sàn trừ vào doanh thu gia sư (dạng phân số, 0.05 = 5%).", updatedByUserId, now, ct);

        context.CommissionConfigHistories.Add(new CommissionConfigHistory
        {
            Parentfeepercent = parentFraction,
            Tutorfeepercent = tutorFraction,
            Changedby = updatedByUserId,
            Changedat = now,
        });

        await context.SaveChangesAsync(ct);

        return await AdminGetAsync(ct);
    }

    private async Task UpsertConfigAsync(
        string key, decimal fraction, string description, string? updatedByUserId, DateTime now, CancellationToken ct)
    {
        var cfg = await context.Systemconfigs.FirstOrDefaultAsync(c => c.Configkey == key, ct);
        if (cfg is null)
        {
            cfg = new Systemconfig { Configkey = key, Description = description };
            context.Systemconfigs.Add(cfg);
        }
        cfg.Configvalue = fraction.ToString("0.####");
        cfg.Updatedby = updatedByUserId;
        cfg.Updatedat = now;
    }

    private static decimal ParseFractionOrDefault(string? value, decimal fallback)
        => decimal.TryParse(value, out var parsed) ? parsed : fallback;
}
