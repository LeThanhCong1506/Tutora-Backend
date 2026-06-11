using Microsoft.AspNetCore.Http;

namespace MV.PresentationLayer.Middlewares;

public class TimezoneMiddleware
{
    private readonly RequestDelegate _next;
    private const string TimezoneHeaderKey = "X-Timezone";
    // Key dùng để share timezone trong HttpContext.Items cho ITimezoneAccessor
    internal const string TimezoneItemKey = "UserTimezone";

    public TimezoneMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var timezone = context.Request.Headers[TimezoneHeaderKey].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(timezone))
        {
            // Set cho TimezoneContext (AsyncLocal) — dùng bởi TimeZoneHelper.ToUserTime() static
            MV.DomainLayer.Helpers.TimezoneContext.CurrentTimezone = timezone;

            // Set cho HttpContext.Items — dùng bởi ITimezoneAccessor (DI injection)
            context.Items[TimezoneItemKey] = timezone;
        }

        await _next(context);
    }
}
