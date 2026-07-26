using System.Globalization;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;
using PayOSPaymentLink = PayOS.Models.V2.PaymentRequests.PaymentLink;

namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// A normalized observation of exactly one real-money transaction.
/// </summary>
public sealed class PaymentTransactionCapture
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    private PaymentTransactionCapture()
    {
    }

    public string PaymentMethod { get; private init; } = PaymentTransactionMethod.Manual;

    public string CaptureSource { get; private init; } = PaymentCaptureSource.Manual;

    public long? ObservedOrderCode { get; private init; }

    public decimal? ObservedAmount { get; private init; }

    public string? ProviderTransactionId { get; private init; }

    public string? PaymentLinkId { get; private init; }

    public string? Currency { get; private init; }

    public DateTime? PaidAtUtc { get; private init; }

    public string? ProcessedBy { get; private init; }

    public string? Note { get; private init; }

    public string? Description { get; private init; }

    public string? WebhookCode { get; private init; }

    public string? WebhookDesc { get; private init; }

    public bool? WebhookSuccess { get; private init; }

    public string? ProviderCode { get; private init; }

    public string? ProviderDesc { get; private init; }

    public string? SourceAccountBankId { get; private init; }

    public string? SourceAccountBankName { get; private init; }

    public string? SourceAccountNumber { get; private init; }

    public string? SourceAccountName { get; private init; }

    public string? DestinationAccountNumber { get; private init; }

    public string? DestinationAccountName { get; private init; }

    public string? DestinationVirtualAccountNumber { get; private init; }

    public string? DestinationVirtualAccountName { get; private init; }

    public string? ProviderPayload { get; private init; }

    public string? WebhookPayload { get; private init; }

    public bool HasPayOSWebhook => WebhookPayload != null;

    public static PaymentTransactionCapture FromPayOSWebhook(
        PaymentWebhookRequest webhook,
        string? rawPayload = null)
    {
        ArgumentNullException.ThrowIfNull(webhook);
        ArgumentNullException.ThrowIfNull(webhook.Data);

        var data = webhook.Data;
        return new PaymentTransactionCapture
        {
            PaymentMethod = PaymentTransactionMethod.PayOS,
            CaptureSource = PaymentCaptureSource.Webhook,
            ObservedOrderCode = data.OrderCode,
            ObservedAmount = data.Amount,
            ProviderTransactionId = NormalizeString(data.Reference),
            PaymentLinkId = NormalizeString(data.PaymentLinkId),
            Currency = NormalizeString(data.Currency) ?? MV.DomainLayer.Constants.Currency.Vnd,
            PaidAtUtc = ParsePayOSDateTime(data.TransactionDateTime),
            Description = NormalizeString(data.Description),
            WebhookCode = NormalizeString(webhook.Code),
            WebhookDesc = NormalizeString(webhook.Desc),
            WebhookSuccess = webhook.Success,
            ProviderCode = NormalizeString(data.Code),
            ProviderDesc = NormalizeString(data.Desc),
            SourceAccountBankId = NormalizeAccountValue(data.CounterAccountBankId),
            SourceAccountBankName = NormalizeAccountValue(data.CounterAccountBankName),
            SourceAccountNumber = NormalizeAccountValue(data.CounterAccountNumber),
            SourceAccountName = NormalizeAccountValue(data.CounterAccountName),
            DestinationAccountNumber = NormalizeAccountValue(data.AccountNumber),
            DestinationAccountName = NormalizeAccountValue(data.AccountName),
            DestinationVirtualAccountNumber = NormalizeAccountValue(data.VirtualAccountNumber),
            DestinationVirtualAccountName = NormalizeAccountValue(data.VirtualAccountName),
            ProviderPayload = JsonSerializer.Serialize(data, PayloadJsonOptions),
            WebhookPayload = NormalizeJsonPayload(rawPayload)
                ?? JsonSerializer.Serialize(webhook, PayloadJsonOptions)
        };
    }

    public static IReadOnlyList<PaymentTransactionCapture> FromPayOSPaymentLink(
        PayOSPaymentLink paymentLink)
    {
        ArgumentNullException.ThrowIfNull(paymentLink);

        if (paymentLink.Transactions == null || paymentLink.Transactions.Count == 0)
            return Array.Empty<PaymentTransactionCapture>();

        var captures = new List<PaymentTransactionCapture>(paymentLink.Transactions.Count);
        foreach (var transaction in paymentLink.Transactions)
        {
            captures.Add(new PaymentTransactionCapture
            {
                PaymentMethod = PaymentTransactionMethod.PayOS,
                CaptureSource = PaymentCaptureSource.Polling,
                ObservedOrderCode = paymentLink.OrderCode,
                ObservedAmount = transaction.Amount,
                ProviderTransactionId = NormalizeString(transaction.Reference),
                PaymentLinkId = NormalizeString(paymentLink.Id),
                Currency = MV.DomainLayer.Constants.Currency.Vnd,
                PaidAtUtc = ParsePayOSDateTime(transaction.TransactionDateTime),
                Description = NormalizeString(transaction.Description),
                SourceAccountBankId = NormalizeAccountValue(transaction.CounterAccountBankId),
                SourceAccountBankName = NormalizeAccountValue(transaction.CounterAccountBankName),
                SourceAccountNumber = NormalizeAccountValue(transaction.CounterAccountNumber),
                SourceAccountName = NormalizeAccountValue(transaction.CounterAccountName),
                DestinationAccountNumber = NormalizeAccountValue(transaction.AccountNumber),
                DestinationVirtualAccountNumber = NormalizeAccountValue(transaction.VirtualAccountNumber),
                DestinationVirtualAccountName = NormalizeAccountValue(transaction.VirtualAccountName),
                ProviderPayload = JsonSerializer.Serialize(transaction, PayloadJsonOptions),
                Note = "Confirmed from PayOS payment-link lookup because the webhook had not updated the booking yet."
            });
        }

        return captures;
    }

    public static PaymentTransactionCapture FromManual(
        DateTimeOffset? paidAt,
        string? processedBy,
        string? note,
        string? providerTransactionId = null)
    {
        return new PaymentTransactionCapture
        {
            PaymentMethod = PaymentTransactionMethod.Manual,
            CaptureSource = PaymentCaptureSource.Manual,
            PaidAtUtc = paidAt?.UtcDateTime,
            ProcessedBy = NormalizeString(processedBy),
            Note = NormalizeString(note),
            ProviderTransactionId = NormalizeString(providerTransactionId)
        };
    }

    public string? GetProviderTransactionId(string? manualFallback = null)
        => ProviderTransactionId
            ?? (PaymentMethod == PaymentTransactionMethod.Manual
                ? NormalizeString(manualFallback)
                : null);

    public string GetProcessingKey()
        => ProviderTransactionId
            ?? $"payos-observation-{BuildFingerprint(
                PaymentMethod,
                ObservedOrderCode,
                PaymentLinkId,
                ObservedAmount ?? 0,
                PaidAtUtc ?? DateTime.UnixEpoch,
                SourceAccountNumber,
                DestinationAccountNumber)}";

    /// <summary>
    /// Builds the provider/fingerprint identity predicate used by both webhook
    /// and polling reconciliation. A real provider reference is authoritative;
    /// fingerprint matching is only a fallback when either observation has no
    /// provider reference yet.
    /// </summary>
    public static Expression<Func<PaymentTransaction, bool>>
        BuildIdentityMatchPredicate(PaymentTransaction incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        var paymentMethod = incoming.Paymentmethod;
        var providerTransactionId = NormalizeString(
            incoming.Providertransactionid);
        var captureFingerprint = NormalizeString(
            incoming.Capturefingerprint);

        return stored => stored.Paymentmethod == paymentMethod
            && ((providerTransactionId != null
                    && stored.Providertransactionid
                        == providerTransactionId)
                || (captureFingerprint != null
                    && (providerTransactionId == null
                        || stored.Providertransactionid == null)
                    && stored.Capturefingerprint
                        == captureFingerprint));
    }

    /// <summary>
    /// Returns a stable identity for downstream idempotency after the
    /// transaction has been persisted. A database row id is both immutable
    /// across provider enrichment and unique when two legitimate provider
    /// transactions happen to share the same observation fingerprint.
    /// </summary>
    public static string? GetStableStoredProcessingKey(
        PaymentTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        return transaction.Paymenttransactionid > 0
            ? $"payment-transaction-{transaction.Paymenttransactionid}"
            : null;
    }

    public PaymentTransaction Create(
        string purpose,
        string direction,
        decimal amount,
        string? userId,
        long? orderCode,
        string? providerTransactionIdFallback = null,
        int? bookingId = null,
        int? withdrawalId = null,
        string? description = null,
        string? destinationAccountNumber = null,
        string? destinationAccountName = null,
        string? destinationBankName = null,
        string? note = null,
        int? paymentRequestId = null,
        string reconciliationStatus = PaymentReconciliationStatus.Matched,
        string? destinationBankBin = null,
        int? aiCreditPackageId = null,
        string? aiCreditUserId = null)
    {
        var providerTransactionId = GetProviderTransactionId(providerTransactionIdFallback);
        var effectiveAmount = ObservedAmount ?? amount;
        var effectiveOrderCode = ObservedOrderCode ?? orderCode;
        var effectivePaidAt = PaidAtUtc ?? TimeZoneHelper.UtcNow;
        var effectiveDestinationNumber = DestinationAccountNumber
            ?? NormalizeAccountValue(destinationAccountNumber);

        return new PaymentTransaction
        {
            Userid = NormalizeString(userId),
            Paymentmethod = PaymentMethod,
            Direction = direction,
            Purpose = purpose,
            Status = PaymentTransactionStatus.Succeeded,
            Amount = effectiveAmount,
            Currency = Currency ?? MV.DomainLayer.Constants.Currency.Vnd,
            Ordercode = effectiveOrderCode,
            Providertransactionid = providerTransactionId,
            Paymentlinkid = PaymentLinkId,
            Paymentrequestid = paymentRequestId,
            Bookingid = bookingId,
            Withdrawalid = withdrawalId,
            AiCreditPackageid = aiCreditPackageId,
            AiCreditUserid = aiCreditUserId,
            Description = Description ?? NormalizeString(description),
            Paidat = effectivePaidAt,
            Createdat = TimeZoneHelper.UtcNow,
            Processedby = ProcessedBy,
            Note = Note ?? NormalizeString(note),
            Capturesource = CaptureSource,
            Reconciliationstatus = reconciliationStatus,
            Capturefingerprint = BuildFingerprint(
                PaymentMethod,
                effectiveOrderCode,
                PaymentLinkId,
                effectiveAmount,
                PaidAtUtc ?? DateTime.UnixEpoch,
                SourceAccountNumber,
                effectiveDestinationNumber),
            Webhookcode = WebhookCode,
            Webhookdesc = WebhookDesc,
            Webhooksuccess = WebhookSuccess,
            Providercode = ProviderCode,
            Providerdesc = ProviderDesc,
            Sourceaccountbankid = SourceAccountBankId,
            Sourceaccountbankname = SourceAccountBankName,
            Sourceaccountnumber = SourceAccountNumber,
            Sourceaccountname = SourceAccountName,
            Destinationaccountbankbin = NormalizeAccountValue(destinationBankBin),
            Destinationaccountbankname = NormalizeAccountValue(destinationBankName),
            Destinationaccountnumber = effectiveDestinationNumber,
            Destinationaccountname = DestinationAccountName
                ?? NormalizeAccountValue(destinationAccountName),
            Destinationvirtualaccountnumber = DestinationVirtualAccountNumber,
            Destinationvirtualaccountname = DestinationVirtualAccountName,
            Providerpayload = ProviderPayload,
            Webhookpayload = WebhookPayload
        };
    }

    private static string? NormalizeString(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeAccountValue(string? value)
    {
        var normalized = NormalizeString(value);
        return normalized is null or "0" ? null : normalized;
    }

    private static string? NormalizeJsonPayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            using var document = JsonDocument.Parse(payload);
            return payload;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DateTime? ParsePayOSDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        var hasExplicitOffset = normalized.EndsWith("Z", StringComparison.OrdinalIgnoreCase)
            || (normalized.Length >= 6
                && (normalized[^6] == '+' || normalized[^6] == '-')
                && normalized[^3] == ':');

        if (hasExplicitOffset
            && DateTimeOffset.TryParse(
                normalized,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var offsetValue))
        {
            return offsetValue.UtcDateTime;
        }

        var formats = new[]
        {
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd'T'HH:mm:ss",
            "yyyy-MM-dd HH:mm:ss.fff",
            "yyyy-MM-dd'T'HH:mm:ss.fff"
        };

        if (DateTime.TryParseExact(
            normalized,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var localValue))
        {
            return TimeZoneHelper.ToUtc(localValue, "Asia/Ho_Chi_Minh");
        }

        return null;
    }

    private static string BuildFingerprint(
        string paymentMethod,
        long? orderCode,
        string? paymentLinkId,
        decimal amount,
        DateTime paidAt,
        string? sourceAccountNumber,
        string? destinationAccountNumber)
    {
        var normalized = string.Join(
            "|",
            paymentMethod,
            orderCode?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            paymentLinkId ?? string.Empty,
            amount.ToString(CultureInfo.InvariantCulture),
            paidAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            sourceAccountNumber ?? string.Empty,
            destinationAccountNumber ?? string.Empty);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))
            .ToLowerInvariant();
    }
}
