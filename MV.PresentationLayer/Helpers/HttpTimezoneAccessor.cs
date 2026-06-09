using Microsoft.AspNetCore.Http;
using MV.ApplicationLayer.Interfaces;
using MV.PresentationLayer.Middlewares;

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
        if (context == null) return "UTC";

        // 1. Đọc từ HttpContext.Items (được set bởi TimezoneMiddleware)
        if (context.Items.TryGetValue(TimezoneMiddleware.TimezoneItemKey, out var timezoneObj)
            && timezoneObj is string tzFromItems
            && !string.IsNullOrWhiteSpace(tzFromItems))
        {
            return tzFromItems;
        }

        // 2. Fallback: đọc thẳng từ header nếu middleware chưa chạy
        var tzFromHeader = context.Request.Headers["X-Timezone"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(tzFromHeader))
            return tzFromHeader;

        // 3. Default UTC
        return "UTC";
    }
}
