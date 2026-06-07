using Microsoft.AspNetCore.Http;
using MV.ApplicationLayer.Interfaces;

namespace MV.PresentationLayer.Helpers;

public class HttpTimezoneAccessor : ITimezoneAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpTimezoneAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string GetCurrentTimezone()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context != null && context.Items.TryGetValue("UserTimezone", out var timezoneObj))
        {
            if (timezoneObj is string timezone && !string.IsNullOrWhiteSpace(timezone))
            {
                return timezone;
            }
        }
        
        // Default to UTC if no timezone is specified or available
        return "UTC";
    }
}
