using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO.ResponseModel.Admin;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;
using MV.ApplicationLayer.Interfaces;

namespace MV.ApplicationLayer.Services;

public class SystemAlertService(IAppDbContext context) : ISystemAlertService
{
    public async Task<SystemAlertResponse> GetAlertsAsync(int page, int pageSize, bool? resolved = null, CancellationToken ct = default)
    {
        var query = context.Systemalerts.AsNoTracking();

        if (resolved.HasValue)
            query = query.Where(a => a.Resolved == resolved.Value);

        var total = await query.CountAsync(ct);

        var rawItems = await query
            .OrderByDescending(a => a.Createdat)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new { a.Alertid, a.Type, a.Severity, a.Message, a.Resolved, a.Resolvedat, a.Resolvedby, a.Createdat })
            .ToListAsync(ct);

        var items = rawItems.Select(a => new SystemAlertItem
        {
            AlertId = a.Alertid,
            Type = a.Type,
            Severity = a.Severity,
            Message = a.Message,
            Resolved = a.Resolved,
            ResolvedAt = a.Resolvedat,
            ResolvedBy = a.Resolvedby,
            CreatedAt = a.Createdat
        }).ToList();

        return new SystemAlertResponse
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<int> CreateAlertAsync(string type, string severity, string message, string? metadata = null, CancellationToken ct = default)
    {
        var alert = new Systemalert
        {
            Type = type,
            Severity = severity,
            Message = message,
            Metadata = metadata,
            Resolved = false,
            Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        };

        context.Systemalerts.Add(alert);
        await context.SaveChangesAsync(ct);

        return alert.Alertid;
    }

    public async Task<bool> ResolveAlertAsync(int alertId, string resolvedBy, CancellationToken ct = default)
    {
        var alert = await context.Systemalerts.FirstOrDefaultAsync(a => a.Alertid == alertId, ct);

        if (alert is null)
            return false;

        alert.Resolved = true;
        alert.Resolvedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        alert.Resolvedby = resolvedBy;

        await context.SaveChangesAsync(ct);
        return true;
    }
}
