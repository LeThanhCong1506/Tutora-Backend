using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.RequestModel.Admin;
using MV.DomainLayer.DTO.ResponseModel.Admin;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class ManualPaymentContractTests
{
    [Fact]
    public void BookingManualConfirmation_RequiresStructuredBankAuditFields()
    {
        var request = new AdminConfirmPaymentRequest();

        var errors = Validate(request);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(request.Amount)));
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(request.TransactionId)));
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(request.PaidAt)));
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(request.Note)));
    }

    [Fact]
    public void BookingManualConfirmation_AcceptsCompleteBankAuditFields()
    {
        var request = new AdminConfirmPaymentRequest
        {
            Amount = 900_000,
            TransactionId = "FT260715123456",
            PaidAt = new DateTimeOffset(2026, 7, 15, 19, 57, 16, TimeSpan.FromHours(7)),
            Note = "Đã đối soát sao kê ngân hàng."
        };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void WithdrawalApproval_RequiresPaidAtNoteBankCodeAndProof()
    {
        var request = new ApproveWithdrawalRequest();

        var errors = Validate(request);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(request.PaidAt)));
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(request.Note)));
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(request.BankTransactionCode)));
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(request.ProofImage)));
    }

    [Fact]
    public void WithdrawalApproval_AcceptsCompleteManualTransferAudit()
    {
        var proof = new FormFile(
            new MemoryStream([137, 80, 78, 71]),
            0,
            4,
            nameof(ApproveWithdrawalRequest.ProofImage),
            "receipt.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };
        var request = new ApproveWithdrawalRequest
        {
            PaidAt = new DateTimeOffset(2026, 7, 17, 15, 30, 0, TimeSpan.FromHours(7)),
            Note = "Đã đối soát đúng số tiền và tài khoản nhận.",
            BankTransactionCode = "FT26082212345678",
            ProofImage = proof
        };

        Assert.Empty(Validate(request));
    }

    [Theory]
    [InlineData("FT26082212345678")]
    [InlineData("ft2608221234")]   // chữ thường hợp lệ; service tự chuẩn hoá về chữ hoa
    [InlineData("2026.08.22/0001")]
    [InlineData("REF_123-456")]
    public void WithdrawalApproval_AcceptsRealisticBankTransactionCodes(string code)
    {
        var request = BuildApproval(code);

        Assert.DoesNotContain(
            Validate(request),
            e => e.MemberNames.Contains(nameof(ApproveWithdrawalRequest.BankTransactionCode)));
    }

    [Theory]
    [InlineData("")]               // bắt buộc
    [InlineData("FT")]             // ngắn hơn 4 ký tự
    [InlineData("FT2608 221234")]  // khoảng trắng — dễ là lỗi dán nhầm cả dòng sao kê
    [InlineData("FT<script>")]     // ký tự lạ
    public void WithdrawalApproval_RejectsMalformedBankTransactionCodes(string code)
    {
        var request = BuildApproval(code);

        Assert.Contains(
            Validate(request),
            e => e.MemberNames.Contains(nameof(ApproveWithdrawalRequest.BankTransactionCode)));
    }

    [Fact]
    public void ApproveWithdrawalRequestContract_SeparatesBankCodeFromInternalPayoutCode()
    {
        // Mã đối soát nội bộ do backend sinh và KHÔNG bao giờ nhận từ client; mã ngân hàng thì
        // ngược lại — staff đọc trên biên lai rồi nhập. Hai thứ này phải là 2 trường tách biệt,
        // gộp lại là mất khả năng đối soát với sao kê.
        var contract = typeof(ApproveWithdrawalRequest);

        Assert.Null(contract.GetProperty("TransactionId"));
        Assert.Null(contract.GetProperty("ProviderTransactionId"));
        Assert.NotNull(contract.GetProperty(nameof(ApproveWithdrawalRequest.BankTransactionCode)));
    }

    /// <summary>Yêu cầu duyệt hợp lệ mọi mặt, chỉ thay mã ngân hàng để soi riêng trường đó.</summary>
    private static ApproveWithdrawalRequest BuildApproval(string bankTransactionCode) => new()
    {
        PaidAt = new DateTimeOffset(2026, 8, 22, 15, 30, 0, TimeSpan.FromHours(7)),
        Note = "Đã đối soát đúng số tiền và tài khoản nhận.",
        BankTransactionCode = bankTransactionCode,
        ProofImage = new FormFile(
            new MemoryStream([137, 80, 78, 71]),
            0,
            4,
            nameof(ApproveWithdrawalRequest.ProofImage),
            "receipt.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        }
    };

    [Fact]
    public void ApproveWithdrawalRequestContract_DoesNotExposeTransactionId()
    {
        // The payout reference is minted by the backend (PayoutCodeGenerator), never typed in by
        // staff/admin — the request DTO must not carry a TransactionId field for the client to set.
        var responseType = typeof(ApproveWithdrawalRequest);

        Assert.Null(responseType.GetProperty("TransactionId"));
    }

    [Fact]
    public void PayoutCodeGenerator_ProducesNonEmptyWithdrawalScopedCode()
    {
        var code = PayoutCodeGenerator.Generate(42);

        Assert.False(string.IsNullOrWhiteSpace(code));
        Assert.StartsWith("WD-", code);
        Assert.Contains("-42-", code);
    }

    [Fact]
    public void WithdrawalRejection_RequiresMeaningfulReason()
    {
        var request = new RejectWithdrawalRequest { Reason = "x" };

        var errors = Validate(request);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(request.Reason)));
    }

    [Fact]
    public void ManualCapture_PreservesTransactionReferenceUtcTimeActorAndDestinationSnapshot()
    {
        var paidAt = new DateTimeOffset(2026, 7, 15, 19, 57, 16, TimeSpan.FromHours(7));
        var capture = PaymentTransactionCapture.FromManual(
            paidAt,
            "staff-user-id",
            "Đã đối soát sao kê ngân hàng.",
            "FT260715123456");

        var transaction = capture.Create(
            PaymentTransactionPurpose.Withdrawal,
            PaymentTransactionDirection.Outbound,
            500_000,
            "tutor-user-id",
            orderCode: null,
            withdrawalId: 12,
            description: "Manual withdrawal payout",
            destinationAccountNumber: "1810342543",
            destinationAccountName: "LE QUOC KHANH",
            destinationBankName: "Vietcombank");

        Assert.Equal("FT260715123456", transaction.Providertransactionid);
        Assert.Equal(paidAt.UtcDateTime, transaction.Paidat);
        Assert.Equal("staff-user-id", transaction.Processedby);
        Assert.Equal("Đã đối soát sao kê ngân hàng.", transaction.Note);
        Assert.Null(transaction.Ordercode);
        Assert.Equal("1810342543", transaction.Destinationaccountnumber);
        Assert.Equal("LE QUOC KHANH", transaction.Destinationaccountname);
        Assert.Equal("Vietcombank", transaction.Destinationaccountbankname);
    }

    [Fact]
    public void WalletServiceContract_DoesNotExposeTopupCreation()
    {
        var methods = typeof(IWalletService).GetMethods();

        Assert.DoesNotContain(methods, method => method.Name == "CreateTopupRequestAsync");
        Assert.Contains(methods, method => method.Name == "ProcessTopupWebhookAsync");
    }

    [Fact]
    public void DomainContract_DoesNotExposeFreeFormTopupRequest()
    {
        var requestType = typeof(TopupStatus).Assembly.GetType(
            "MV.DomainLayer.DTO.RequestModel.TopupRequest");

        Assert.Null(requestType);
    }

    [Fact]
    public void AdminPaymentTransactionContract_ExposesCaptureFingerprint()
    {
        var response = new AdminPaymentTransactionItem
        {
            CaptureFingerprint = new string('a', 64)
        };

        Assert.Equal(new string('a', 64), response.CaptureFingerprint);
    }

    [Fact]
    public void AdminPaymentTransactionContract_UsesPaymentMethodInsteadOfChannel()
    {
        var responseType = typeof(AdminPaymentTransactionItem);

        Assert.NotNull(responseType.GetProperty(nameof(AdminPaymentTransactionItem.PaymentMethod)));
        Assert.Null(responseType.GetProperty("Channel"));
    }

    [Fact]
    public void AdminWithdrawalListContract_ExposesTutorAndBankSnapshot()
    {
        var responseType = typeof(MV.DomainLayer.DTO.ResponseModel.WithdrawalItem);

        Assert.NotNull(responseType.GetProperty("TutorName"));
        Assert.NotNull(responseType.GetProperty("TutorEmail"));
        Assert.NotNull(responseType.GetProperty("BankName"));
        Assert.NotNull(responseType.GetProperty("AccountNumber"));
    }

    private static IReadOnlyList<ValidationResult> Validate(object value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), results, validateAllProperties: true);
        return results;
    }
}
