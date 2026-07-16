using System.Globalization;
using System.Text.Json;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;
using PayOSPaymentLink = PayOS.Models.V2.PaymentRequests.PaymentLink;

namespace MV.ApplicationLayer.Helpers;

public sealed class PaymentTransactionCapture
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    private PaymentTransactionCapture()
    {
    }

    public string Channel { get; private init; } = PaymentTransactionChannel.Manual;

    public PaymentWebhookRequest? PayOSWebhook { get; private init; }

    public PayOSPaymentLink? PayOSPaymentLink { get; private init; }

    public DateTime? PaidAt { get; private init; }

    public string? ProcessedBy { get; private init; }

    public string? Note { get; private init; }

    public string? ProviderTransactionId { get; private init; }

    public bool HasPayOSWebhook => PayOSWebhook != null;

    public static PaymentTransactionCapture FromPayOSWebhook(PaymentWebhookRequest webhook)
    {
        return new PaymentTransactionCapture
        {
            Channel = PaymentTransactionChannel.PayOS,
            PayOSWebhook = webhook
        };
    }

    public static PaymentTransactionCapture FromPayOSPaymentLink(PayOSPaymentLink paymentLink)
    {
        var transaction = paymentLink.Transactions?.LastOrDefault();
        return new PaymentTransactionCapture
        {
            Channel = PaymentTransactionChannel.PayOS,
            PayOSPaymentLink = paymentLink,
            PaidAt = ParsePayOSDateTime(transaction?.TransactionDateTime),
            ProviderTransactionId = NormalizeString(transaction?.Reference),
            Note = "Confirmed from PayOS payment-link lookup because the webhook had not updated the booking yet."
        };
    }

    public static PaymentTransactionCapture FromManual(
        DateTime? paidAt,
        string? processedBy,
        string? note,
        string? providerTransactionId = null)
    {
        return new PaymentTransactionCapture
        {
            Channel = PaymentTransactionChannel.Manual,
            PaidAt = NormalizeDateTime(paidAt),
            ProcessedBy = NormalizeString(processedBy),
            Note = NormalizeString(note),
            ProviderTransactionId = NormalizeString(providerTransactionId)
        };
    }

    public string? GetProviderTransactionId(string? fallback)
    {
        var data = PayOSWebhook?.Data;
        return NormalizeString(ProviderTransactionId)
            ?? NormalizeString(data?.Reference)
            ?? NormalizeString(fallback);
    }

    public PaymentTransaction Create(
        string purpose,
        string direction,
        decimal amount,
        string? userId,
        long? orderCode,
        string? providerTransactionIdFallback = null,
        int? bookingId = null,
        int? topupRequestId = null,
        int? withdrawalId = null,
        string? description = null,
        string? destinationAccountNumber = null,
        string? destinationAccountName = null,
        string? destinationBankName = null,
        string? note = null)
    {
        var data = PayOSWebhook?.Data;
        var paymentLink = PayOSPaymentLink;
        var lookupTransaction = paymentLink?.Transactions?.LastOrDefault();
        var paidAt = PaidAt
            ?? ParsePayOSDateTime(data?.TransactionDateTime)
            ?? TimeZoneHelper.UtcNow;

        return new PaymentTransaction
        {
            Userid = NormalizeString(userId),
            Channel = Channel,
            Direction = direction,
            Purpose = purpose,
            Status = PaymentTransactionStatus.Succeeded,
            Amount = amount,
            Currency = NormalizeString(data?.Currency) ?? Currency.Vnd,
            Ordercode = orderCode,
            Providertransactionid = GetProviderTransactionId(providerTransactionIdFallback),
            Paymentlinkid = NormalizeString(data?.PaymentLinkId) ?? NormalizeString(paymentLink?.Id),
            Bookingid = bookingId,
            Topuprequestid = topupRequestId,
            Withdrawalid = withdrawalId,
            Description = NormalizeString(description)
                ?? NormalizeString(data?.Description)
                ?? NormalizeString(lookupTransaction?.Description),
            Paidat = paidAt,
            Createdat = TimeZoneHelper.UtcNow,
            Processedby = ProcessedBy,
            Note = NormalizeString(note) ?? Note,
            Webhookcode = NormalizeString(PayOSWebhook?.Code),
            Webhookdesc = NormalizeString(PayOSWebhook?.Desc),
            Webhooksuccess = PayOSWebhook?.Success,
            Providercode = NormalizeString(data?.Code),
            Providerdesc = NormalizeString(data?.Desc),
            Sourceaccountbankid = NormalizeString(data?.CounterAccountBankId)
                ?? NormalizeString(lookupTransaction?.CounterAccountBankId),
            Sourceaccountbankname = NormalizeString(data?.CounterAccountBankName)
                ?? NormalizeString(lookupTransaction?.CounterAccountBankName),
            Sourceaccountnumber = NormalizeString(data?.CounterAccountNumber)
                ?? NormalizeString(lookupTransaction?.CounterAccountNumber),
            Sourceaccountname = NormalizeString(data?.CounterAccountName)
                ?? NormalizeString(lookupTransaction?.CounterAccountName),
            Destinationaccountnumber = NormalizeString(data?.VirtualAccountNumber)
                ?? NormalizeString(data?.AccountNumber)
                ?? NormalizeString(lookupTransaction?.VirtualAccountNumber)
                ?? NormalizeString(lookupTransaction?.AccountNumber)
                ?? NormalizeString(destinationAccountNumber),
            Destinationaccountname = NormalizeString(data?.VirtualAccountName)
                ?? NormalizeString(lookupTransaction?.VirtualAccountName)
                ?? NormalizeString(destinationAccountName),
            Destinationaccountbankname = NormalizeString(destinationBankName),
            Providerpayload = PayOSWebhook != null
                ? JsonSerializer.Serialize(PayOSWebhook, PayloadJsonOptions)
                : PayOSPaymentLink != null
                    ? JsonSerializer.Serialize(PayOSPaymentLink, PayloadJsonOptions)
                    : null
        };
    }

    private static string? NormalizeString(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime? NormalizeDateTime(DateTime? value)
    {
        if (!value.HasValue)
            return null;

        return value.Value.Kind == DateTimeKind.Utc
            ? value.Value
            : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
    }

    private static DateTime? ParsePayOSDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? NormalizeDateTime(parsed)
            : null;
    }
}
