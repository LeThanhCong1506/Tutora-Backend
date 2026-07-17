using MV.ApplicationLayer.Helpers;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class PaymentTransactionCaptureTests
{
    [Fact]
    public void PayOSWebhook_StoresProviderFieldsAndSeparatesActualFromVirtualDestination()
    {
        const string rawPayload =
            "  { \"code\": \"00\", \"desc\": \"success\", \"success\": true, \"signature\": \"signed\" }\r\n";
        var webhook = new PaymentWebhookRequest
        {
            Code = "00",
            Desc = "success",
            Success = true,
            Data = new PayOSWebhookData
            {
                OrderCode = 123456789,
                Amount = 945_000,
                Description = "Thanh toan booking",
                AccountNumber = "1810342543",
                AccountName = "CONG TY TUTORA",
                Reference = "FT260715123456",
                TransactionDateTime = "2026-07-15 19:57:16",
                Currency = "VND",
                PaymentLinkId = "380077d2b19949318b9ebfe4274b4359",
                Code = "00",
                Desc = "success",
                CounterAccountBankId = "0",
                CounterAccountBankName = "0",
                CounterAccountNumber = "0123456789",
                CounterAccountName = "LE MINH AN",
                VirtualAccountNumber = "V3CAS1810342543",
                VirtualAccountName = "TUTORA BOOKING"
            }
        };

        var capture = PaymentTransactionCapture.FromPayOSWebhook(webhook, rawPayload);
        var transaction = capture.Create(
            PaymentTransactionPurpose.BookingDeposit,
            PaymentTransactionDirection.Inbound,
            945_000,
            "parent-id",
            webhook.Data.OrderCode,
            bookingId: 42,
            paymentRequestId: 7,
            destinationBankBin: "970436",
            destinationBankName: "Vietcombank");

        Assert.Equal("FT260715123456", transaction.Providertransactionid);
        Assert.Equal(123456789, transaction.Ordercode);
        Assert.Equal("380077d2b19949318b9ebfe4274b4359", transaction.Paymentlinkid);
        Assert.Equal(945_000, transaction.Amount);
        Assert.Equal(new DateTime(2026, 7, 15, 12, 57, 16, DateTimeKind.Utc), transaction.Paidat);
        Assert.Equal(PaymentCaptureSource.Webhook, transaction.Capturesource);
        Assert.Equal(PaymentReconciliationStatus.Matched, transaction.Reconciliationstatus);
        Assert.Equal("00", transaction.Webhookcode);
        Assert.Equal("success", transaction.Webhookdesc);
        Assert.True(transaction.Webhooksuccess);
        Assert.Equal("00", transaction.Providercode);
        Assert.Equal("success", transaction.Providerdesc);
        Assert.Null(transaction.Sourceaccountbankid);
        Assert.Null(transaction.Sourceaccountbankname);
        Assert.Equal("0123456789", transaction.Sourceaccountnumber);
        Assert.Equal("LE MINH AN", transaction.Sourceaccountname);
        Assert.Equal("970436", transaction.Destinationaccountbankbin);
        Assert.Equal("Vietcombank", transaction.Destinationaccountbankname);
        Assert.Equal("1810342543", transaction.Destinationaccountnumber);
        Assert.Equal("CONG TY TUTORA", transaction.Destinationaccountname);
        Assert.Equal("V3CAS1810342543", transaction.Destinationvirtualaccountnumber);
        Assert.Equal("TUTORA BOOKING", transaction.Destinationvirtualaccountname);
        Assert.Contains("\"reference\":\"FT260715123456\"", transaction.Providerpayload);
        Assert.Equal(rawPayload, transaction.Webhookpayload);
    }

    [Fact]
    public void PayOSObservationWithoutReference_DoesNotInventProviderIdAndHasStableFingerprint()
    {
        var webhook = new PaymentWebhookRequest
        {
            Code = "00",
            Success = true,
            Data = new PayOSWebhookData
            {
                OrderCode = 123456789,
                Amount = 100_000,
                AccountNumber = "1810342543",
                Reference = " ",
                TransactionDateTime = "",
                PaymentLinkId = "link-id"
            }
        };

        var first = PaymentTransactionCapture.FromPayOSWebhook(webhook).Create(
            PaymentTransactionPurpose.BookingDeposit,
            PaymentTransactionDirection.Inbound,
            100_000,
            "parent-id",
            webhook.Data.OrderCode);
        var second = PaymentTransactionCapture.FromPayOSWebhook(webhook).Create(
            PaymentTransactionPurpose.BookingDeposit,
            PaymentTransactionDirection.Inbound,
            100_000,
            "parent-id",
            webhook.Data.OrderCode);

        Assert.Null(first.Providertransactionid);
        Assert.NotNull(first.Capturefingerprint);
        Assert.Equal(64, first.Capturefingerprint!.Length);
        Assert.Equal(first.Capturefingerprint, second.Capturefingerprint);
    }

    [Fact]
    public void PayOSWebhook_MissingDestinationName_UsesPaymentRequestDisplayAccountOnly()
    {
        var webhook = new PaymentWebhookRequest
        {
            Code = "00",
            Success = true,
            Data = new PayOSWebhookData
            {
                OrderCode = 123456789,
                Amount = 100_000,
                AccountNumber = " ",
                AccountName = null,
                Reference = "FT260716000002",
                PaymentLinkId = "link-id",
                CounterAccountBankId = "",
                CounterAccountBankName = "",
                CounterAccountNumber = "",
                CounterAccountName = "",
                VirtualAccountNumber = "",
                VirtualAccountName = ""
            }
        };

        var transaction = PaymentTransactionCapture
            .FromPayOSWebhook(webhook)
            .Create(
                PaymentTransactionPurpose.BookingDeposit,
                PaymentTransactionDirection.Inbound,
                100_000,
                "parent-id",
                webhook.Data.OrderCode,
                destinationAccountNumber: "1810342543",
                destinationAccountName: "CONG TY TUTORA");

        Assert.Equal("1810342543", transaction.Destinationaccountnumber);
        Assert.Equal("CONG TY TUTORA", transaction.Destinationaccountname);
        Assert.Null(transaction.Sourceaccountbankid);
        Assert.Null(transaction.Sourceaccountbankname);
        Assert.Null(transaction.Sourceaccountnumber);
        Assert.Null(transaction.Sourceaccountname);
        Assert.Null(transaction.Destinationvirtualaccountnumber);
        Assert.Null(transaction.Destinationvirtualaccountname);
    }

    [Fact]
    public void StoredProcessingKey_RemainsStableWhenProviderReferenceIsEnriched()
    {
        var transaction = new MV.DomainLayer.Entities.PaymentTransaction
        {
            Paymenttransactionid = 41,
            Paymentmethod = PaymentTransactionMethod.PayOS,
            Capturefingerprint = new string('a', 64)
        };

        var beforeEnrichment = PaymentTransactionCapture
            .GetStableStoredProcessingKey(transaction);
        transaction.Providertransactionid = "FT260716000001";
        var afterEnrichment = PaymentTransactionCapture
            .GetStableStoredProcessingKey(transaction);

        Assert.Equal("payment-transaction-41", beforeEnrichment);
        Assert.Equal(beforeEnrichment, afterEnrichment);
    }

    [Fact]
    public void StoredProcessingKey_DiffersForDistinctRowsWithSameFingerprint()
    {
        var fingerprint = new string('d', 64);
        var first = new MV.DomainLayer.Entities.PaymentTransaction
        {
            Paymenttransactionid = 41,
            Paymentmethod = PaymentTransactionMethod.PayOS,
            Providertransactionid = "REFERENCE-1",
            Capturefingerprint = fingerprint
        };
        var second = new MV.DomainLayer.Entities.PaymentTransaction
        {
            Paymenttransactionid = 42,
            Paymentmethod = PaymentTransactionMethod.PayOS,
            Providertransactionid = "REFERENCE-2",
            Capturefingerprint = fingerprint
        };

        var firstKey = PaymentTransactionCapture
            .GetStableStoredProcessingKey(first);
        var secondKey = PaymentTransactionCapture
            .GetStableStoredProcessingKey(second);

        Assert.Equal("payment-transaction-41", firstKey);
        Assert.Equal("payment-transaction-42", secondKey);
        Assert.NotEqual(firstKey, secondKey);
    }

    [Fact]
    public void IdentityPredicate_DoesNotMergeDifferentProviderReferencesWithSameFingerprint()
    {
        var fingerprint = new string('b', 64);
        var incoming = new MV.DomainLayer.Entities.PaymentTransaction
        {
            Paymentmethod = PaymentTransactionMethod.PayOS,
            Providertransactionid = "REFERENCE-2",
            Capturefingerprint = fingerprint
        };
        var stored = new MV.DomainLayer.Entities.PaymentTransaction
        {
            Paymentmethod = PaymentTransactionMethod.PayOS,
            Providertransactionid = "REFERENCE-1",
            Capturefingerprint = fingerprint
        };

        var matches = PaymentTransactionCapture
            .BuildIdentityMatchPredicate(incoming)
            .Compile();

        Assert.False(matches(stored));
    }

    [Fact]
    public void IdentityPredicate_UsesFingerprintWhenStoredReferenceIsMissing()
    {
        var fingerprint = new string('c', 64);
        var incoming = new MV.DomainLayer.Entities.PaymentTransaction
        {
            Paymentmethod = PaymentTransactionMethod.PayOS,
            Providertransactionid = "REFERENCE-2",
            Capturefingerprint = fingerprint
        };
        var stored = new MV.DomainLayer.Entities.PaymentTransaction
        {
            Paymentmethod = PaymentTransactionMethod.PayOS,
            Providertransactionid = null,
            Capturefingerprint = fingerprint
        };

        var matches = PaymentTransactionCapture
            .BuildIdentityMatchPredicate(incoming)
            .Compile();

        Assert.True(matches(stored));
    }
}
