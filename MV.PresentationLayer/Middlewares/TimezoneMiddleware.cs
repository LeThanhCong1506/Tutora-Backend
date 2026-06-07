using Microsoft.AspNetCore.Http;

namespace MV.PresentationLayer.Middlewares;

public class TimezoneMiddleware
{
    private readonly RequestDelegate _next;
    private const string TimezoneHeaderKey = "X-Timezone";

    public TimezoneMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var timezone = context.Request.Headers[TimezoneHeaderKey].FirstOrDefault();
        
        if (!string.IsNullOrWhiteSpace(timezone))
        {
            MV.DomainLayer.Helpers.TimezoneContext.CurrentTimezone = timezone;
        }

        await _next(context);
    }
}
