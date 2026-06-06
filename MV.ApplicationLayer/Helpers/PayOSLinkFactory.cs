using PayOS;
using PayOS.Models.V2.PaymentRequests;

namespace MV.ApplicationLayer.Helpers;

public class PayOSLinkFactory(PayOSClient payOS, string returnUrl, string cancelUrl)
{
    public async Task<CreatePaymentLinkResponse> CreatePaymentLink(long orderCode, int amount, string description, int? expiredAt)
    {
        var desc = description.Length > 25 ? description[..25] : description;
        return await payOS.PaymentRequests.CreateAsync(new CreatePaymentLinkRequest
        {
            OrderCode = orderCode,
            Amount = amount,
            Description = desc,
            ReturnUrl = returnUrl,
            CancelUrl = cancelUrl,
            ExpiredAt = expiredAt ?? (int)DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeSeconds()
        });
    }
}
