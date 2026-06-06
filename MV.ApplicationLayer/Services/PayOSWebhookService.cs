using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MV.DomainLayer.Constants;
using PayOS;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Service for PayOS webhook signature verification
/// </summary>
public class PayOSWebhookService([FromKeyedServices(ServiceKeys.PayOS.Checkout)] PayOSClient payOS, ILogger<PayOSWebhookService> logger)
{
    public async Task<bool> VerifyWebhookSignatureAsync(string payload)
    {
        try
        {
            var webhook = System.Text.Json.JsonSerializer.Deserialize<PayOS.Models.Webhooks.Webhook>(payload);
            return webhook != null && await payOS.Webhooks.VerifyAsync(webhook) != null;
        }
        catch (PayOS.Exceptions.PayOSException ex)
        {
            logger.LogWarning(ex, "PayOS webhook verification failed: {Message}", ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during webhook verification");
            return false;
        }
    }
}
