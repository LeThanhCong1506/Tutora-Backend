using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Helpers;
using MV.DomainLayer.Interfaces;
using MV.DomainLayer.Settings;
using PayOS;
using PayOS.Models;
using PayOS.Models.V1.Payouts;

namespace MV.InfrastructureLayer.ExternalServices;

public class PayOSTransferClient : IPayOSTransferClient
{
    private readonly PayOSClient _payOS;
    private readonly ILogger<PayOSTransferClient> _logger;
    private readonly BankVerificationSettings _settings;

    public PayOSTransferClient(
        [FromKeyedServices(ServiceKeys.PayOS.Payout)] PayOSClient payOS,
        ILogger<PayOSTransferClient> logger,
        IOptions<BankVerificationSettings> settings)
    {
        _payOS = payOS;
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<TransferResponse> TransferAsync(TransferRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            if (request.Amount <= 0)
                return CreateErrorResponse(BankVerificationConstants.ErrorCodes.ApiError, "Số tiền chuyển không hợp lệ");

            // Use consistent reference for both idempotency and ReferenceId
            var reference = string.IsNullOrWhiteSpace(request.Reference)
                ? Guid.NewGuid().ToString("N")
                : request.Reference;

            var idempotencyKey = $"payout-{reference}";

            var payoutRequest = new PayoutRequest
            {
                ReferenceId = reference,
                Amount = decimal.ToInt64(decimal.Truncate(request.Amount)),
                Description = string.IsNullOrWhiteSpace(request.Description)
                    ? _settings.MicroDepositDescription
                    : request.Description,
                ToBin = request.ToBin,
                ToAccountNumber = request.ToAccountNumber,
                Category = [_settings.MicroDepositDescription.Contains("VERIFY", StringComparison.OrdinalIgnoreCase) ? "other" : "salary"]
            };

            _logger.LogInformation(
                "Creating payout: ReferenceId={ReferenceId}, Amount={Amount}, ToBin={ToBin}, ToAccount={ToAccount}",
                payoutRequest.ReferenceId, payoutRequest.Amount, payoutRequest.ToBin, MaskAccountNumber(payoutRequest.ToAccountNumber));

            var payout = await _payOS.Payouts.CreateAsync(
                payoutRequest,
                idempotencyKey,
                new RequestOptions<Payout>
                {
                    CancellationToken = cancellationToken,
                    Signature = new SignatureOptions
                    {
                        Request = RequestSignatureTypes.Header,
                        Response = ResponseSignatureTypes.Header
                    }
                });

            _logger.LogInformation(
                "Payout created: PayoutId={PayoutId}, ApprovalState={ApprovalState}, TransactionCount={TxCount}",
                payout.Id, payout.ApprovalState, payout.Transactions?.Count ?? 0);

            var transaction = payout.Transactions?.FirstOrDefault();
            var isFailed = IsPayoutFailed(transaction, payout);

            if (isFailed)
            {
                _logger.LogError(
                    "PayOS payout failed. PayoutId={PayoutId}, ApprovalState={ApprovalState}, TxState={TxState}, ErrorCode={ErrorCode}, Error={Error}",
                    payout.Id, payout.ApprovalState, transaction?.State, transaction?.ErrorCode, transaction?.ErrorMessage);

                return CreateErrorResponse(
                    transaction?.ErrorCode ?? BankVerificationConstants.ErrorCodes.DepositFailed,
                    string.IsNullOrWhiteSpace(transaction?.ErrorMessage)
                        ? BankVerificationConstants.ErrorMessages.DepositFailed
                        : transaction.ErrorMessage);
            }

            return new TransferResponse
            {
                IsSuccess = true,
                TransactionId = transaction?.Id ?? payout.Id ?? string.Empty,
                Status = transaction?.State.ToString() ?? payout.ApprovalState.ToString(),
                Amount = request.Amount,
                Reference = transaction?.Reference ?? payout.ReferenceId
            };
        }
        catch (PayOS.Exceptions.ApiException apiEx)
        {
            var statusCode = Convert.ToInt32(apiEx.StatusCode);

            _logger.LogError(apiEx,
                "PayOS API error: StatusCode={StatusCode}, ErrorCode={ErrorCode}, Message={Message}",
                statusCode, apiEx.ErrorCode, apiEx.Message);

            if (statusCode == 403)
            {
                return CreateErrorResponse(
                    BankVerificationConstants.ErrorCodes.DepositFailed,
                    "PayOS từ chối yêu cầu chuyển khoản (403). Vui lòng kiểm tra cấu hình Payout credentials hoặc quyền payout của merchant.");
            }

            return CreateErrorResponse(
                apiEx.ErrorCode ?? BankVerificationConstants.ErrorCodes.ApiError,
                apiEx.Message);
        }
        catch (PayOS.Exceptions.PayOSException payOSEx)
        {
            _logger.LogError(payOSEx, "PayOS SDK error: {Message}", payOSEx.Message);
            return CreateErrorResponse(BankVerificationConstants.ErrorCodes.ApiError, payOSEx.Message);
        }
        catch (TaskCanceledException ex)
        {
            // Distinguish between timeout and caller cancellation
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("PayOS payout cancelled by caller");
                throw new OperationCanceledException("Payout operation was cancelled", ex, cancellationToken);
            }

            _logger.LogWarning("PayOS payout timeout");
            return CreateErrorResponse(
                BankVerificationConstants.ErrorCodes.ApiTimeout,
                BankVerificationConstants.ErrorMessages.ApiTimeout);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error calling PayOS payout API");
            return CreateErrorResponse(
                BankVerificationConstants.ErrorCodes.ApiError,
                BankVerificationConstants.ErrorMessages.ApiError);
        }
    }

    private static bool IsPayoutFailed(PayoutTransaction? transaction, Payout payout) =>
        transaction?.State is PayoutTransactionState.Failed or PayoutTransactionState.Cancelled or PayoutTransactionState.Reversed
        || payout.ApprovalState is PayoutApprovalState.Rejected or PayoutApprovalState.Failed;

    private static TransferResponse CreateErrorResponse(string errorCode, string errorMessage) =>
        new() { IsSuccess = false, ErrorCode = errorCode, ErrorMessage = errorMessage };

    public async Task<PayoutStatusResponse> GetPayoutStatusAsync(string payoutId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(payoutId))
                return new PayoutStatusResponse { IsSuccess = false, ErrorCode = "INVALID_PAYOUT_ID", ErrorMessage = "Mã giao dịch không hợp lệ" };

            _logger.LogInformation("Getting payout status: PayoutId={PayoutId}", payoutId);

            var payout = await _payOS.Payouts.GetAsync(
                payoutId,
                new RequestOptions<Payout>
                {
                    CancellationToken = cancellationToken,
                    Signature = new SignatureOptions
                    {
                        Response = ResponseSignatureTypes.Header
                    }
                });

            var transaction = payout.Transactions?.FirstOrDefault();

            _logger.LogInformation(
                "Payout status retrieved: PayoutId={PayoutId}, ApprovalState={ApprovalState}, TxState={TxState}",
                payout.Id, payout.ApprovalState, transaction?.State);

            return new PayoutStatusResponse
            {
                IsSuccess = true,
                Status = transaction?.State.ToString() ?? payout.ApprovalState.ToString(),
                TransactionId = transaction?.Id ?? payout.Id,
                Amount = transaction?.Amount
            };
        }
        catch (PayOS.Exceptions.ApiException apiEx)
        {
            _logger.LogError(apiEx, "PayOS API error getting payout status: StatusCode={StatusCode}, ErrorCode={ErrorCode}",
                apiEx.StatusCode, apiEx.ErrorCode);
            return new PayoutStatusResponse
            {
                IsSuccess = false,
            ErrorCode = apiEx.ErrorCode ?? BankVerificationConstants.ErrorCodes.ApiError,
                ErrorMessage = apiEx.Message
            };
        }
        catch (TaskCanceledException ex)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Get payout status cancelled by caller");
                throw new OperationCanceledException("Get payout status operation was cancelled", ex, cancellationToken);
            }

            _logger.LogWarning("Get payout status timeout");
            return new PayoutStatusResponse
            {
                IsSuccess = false,
                ErrorCode = BankVerificationConstants.ErrorCodes.ApiTimeout,
                ErrorMessage = BankVerificationConstants.ErrorMessages.ApiTimeout
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting payout status");
            return new PayoutStatusResponse
            {
                IsSuccess = false,
            ErrorCode = PayoutConstants.ErrorCodes.Unknown,
                ErrorMessage = "Lỗi không xác định khi lấy trạng thái giao dịch"
            };
        }
    }

    private string MaskAccountNumber(string? accountNumber) =>
        AccountMaskingHelper.MaskAccountNumber(accountNumber, _settings);
}
