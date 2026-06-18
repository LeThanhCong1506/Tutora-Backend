using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.BankVerification;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using MV.DomainLayer.Interfaces;
using MV.DomainLayer.Settings;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MV.ApplicationLayer.Services;

public partial class BankVerificationService(
    IVietQRClient vietQRClient,
    IPayOSTransferClient payOSTransferClient,
    IUnitOfWork unitOfWork,
    IMemoryCache memoryCache,
    IDistributedCache cache,
    IOptions<VietQRSettings> vietQRSettings,
    IOptions<BankVerificationSettings> bankVerificationSettings,
    IWalletService walletService,
    ILogger<BankVerificationService> logger) : IBankVerificationService
{
    private readonly VietQRSettings _vietQRSettings = vietQRSettings.Value;
    private readonly BankVerificationSettings _settings = bankVerificationSettings.Value;

    [GeneratedRegex(@"^\d{8,19}$")]
    private static partial Regex AccountNumberRegex();

    private static readonly SemaphoreSlim _cacheLock = new(1, 1);
    private const string L1_CACHE_KEY = "bank-list:l1";
    private const string L2_CACHE_KEY = "bank-list:l2";
    private const string REFRESH_LOCK_KEY = "bank-list:refresh-lock";

    public async Task<RequestVerifyResponse> RequestVerificationAsync(
        string userId,
        RequestVerifyRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateAccountNumber(request.AccountNumber);

        var tutor = await unitOfWork.TutorRepository.GetTutorProfileByUserIdAsync(userId, cancellationToken)
            ?? throw new TutorProfileNotFoundException();

        if (tutor.Isbankverified == true &&
            tutor.Bankaccountnumber == request.AccountNumber &&
            tutor.Bankname == request.BankCode)
        {
            throw new AlreadyVerifiedException(BankVerificationConstants.ErrorMessages.AlreadyVerified);
        }

        await CheckVerificationRequestRateLimitAsync(userId, cancellationToken);

        var verificationCost = _settings.MicroDepositAmount;
        var hasSufficientBalance = await walletService.HasSufficientBalanceForVerificationAsync(userId, verificationCost);

        if (!hasSufficientBalance)
        {
            return new RequestVerifyResponse
            {
                IsSuccess = false,
                ErrorCode = BankVerificationConstants.ErrorCodes.InsufficientWalletBalance,
                ErrorMessage = BankVerificationConstants.ErrorMessages.InsufficientWalletBalance
            };
        }

        var verificationCode = GenerateVerificationCode();
        var description = $"{BankVerificationConstants.VerificationCodePrefix}-{verificationCode}";

        var banks = await GetBankListAsync(cancellationToken);
        var bank = banks.FirstOrDefault(b => b.Code == request.BankCode);
        if (bank?.Bin == null)
        {
            return new RequestVerifyResponse
            {
                IsSuccess = false,
                ErrorCode = BankVerificationConstants.ErrorCodes.InvalidBankCode,
                ErrorMessage = BankVerificationConstants.ErrorMessages.InvalidBankCode
            };
        }

        try
        {
            await walletService.DeductVerificationFeeAsync(userId, verificationCost, verificationCode);
        }
        catch (BookingException ex) when (ex.ErrorCode == WalletErrorCodes.InsufficientBalanceForVerification)
        {
            return new RequestVerifyResponse
            {
                IsSuccess = false,
                ErrorCode = BankVerificationConstants.ErrorCodes.InsufficientWalletBalance,
                ErrorMessage = BankVerificationConstants.ErrorMessages.InsufficientWalletBalance
            };
        }

        var transferRequest = new TransferRequest
        {
            Amount = _settings.MicroDepositAmount,
            ToBin = bank.Bin,
            ToAccountNumber = request.AccountNumber,
            Description = description,
            Reference = verificationCode
        };

        var transferResponse = await payOSTransferClient.TransferAsync(transferRequest, cancellationToken);

        if (!transferResponse.IsSuccess)
        {
            return new RequestVerifyResponse
            {
                IsSuccess = false,
                ErrorCode = transferResponse.ErrorCode,
                ErrorMessage = transferResponse.ErrorMessage
            };
        }

        var oldBankName = tutor.Bankname;
        var oldAccountNumber = tutor.Bankaccountnumber;

        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        tutor.Bankname = request.BankCode;
        tutor.Bankaccountnumber = request.AccountNumber;
        tutor.Bankverifycode = verificationCode;
        tutor.Bankverifyrequested = now;
        tutor.Bankverifyattempts = 0;
        tutor.Bankverifystatus = BankVerificationConstants.Statuses.ReadyToConfirm;
        tutor.Isbankverified = false;

        if (_settings.EnableAuditTrail)
        {
            await unitOfWork.TutorRepository.AddBankChangeLogAsync(new BankChangeLog
            {
                Tutorid = tutor.Tutorid,
                Oldbankname = oldBankName,
                Oldbankaccountnumber = oldAccountNumber,
                Newbankname = request.BankCode,
                Newbankaccountnumber = request.AccountNumber,
                Changedat = now,
                Reason = "verification_requested"
            }, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync();
        await IncrementVerificationRequestRateLimitAsync(userId, cancellationToken);

        logger.LogInformation("Verification requested for user {UserId}: {BankCode}/{AccountNumber}",
            userId, request.BankCode, MaskAccountNumber(request.AccountNumber));

        return new RequestVerifyResponse
        {
            IsSuccess = true,
            ExpiresAt = now.AddHours(_settings.VerificationCodeExpiryHours),
            Message = $"Phí xác minh {verificationCost:N0} VND đã được trừ. Vui lòng kiểm tra tài khoản ngân hàng để lấy mã xác minh."
        };
    }

    public async Task<ConfirmVerifyResponse> ConfirmVerificationAsync(
        string userId,
        ConfirmVerifyRequest request,
        CancellationToken cancellationToken = default)
    {
        var tutor = await unitOfWork.TutorRepository.GetTutorProfileByUserIdAsync(userId, cancellationToken)
            ?? throw new TutorProfileNotFoundException();

        if (tutor.Bankverifystatus != BankVerificationConstants.Statuses.ReadyToConfirm &&
            tutor.Bankverifystatus != BankVerificationConstants.Statuses.PendingDeposit)
        {
            return new ConfirmVerifyResponse
            {
                IsVerified = false,
                ErrorMessage = "Không tìm thấy yêu cầu xác minh nào đang chờ xử lý."
            };
        }

        if (tutor.Bankverifyrequested.HasValue)
        {
            var expiryTime = tutor.Bankverifyrequested.Value.AddHours(_settings.VerificationCodeExpiryHours);
            if (MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow > expiryTime)
            {
                tutor.Bankverifystatus = BankVerificationConstants.Statuses.Failed;
                await unitOfWork.SaveChangesAsync();
                throw new VerificationExpiredException(BankVerificationConstants.ErrorMessages.VerificationExpired);
            }
        }

        if (tutor.Bankverifyattempts >= _settings.MaxVerificationAttempts)
        {
            throw new BankVerificationException(
                BankVerificationConstants.ErrorMessages.VerificationLocked,
                BankVerificationConstants.ErrorCodes.VerificationLocked);
        }

        var inputCode = request.VerificationCode?.Trim() ?? "";
        var prefixWithDash = $"{BankVerificationConstants.VerificationCodePrefix}-";
        var prefixNoDash = BankVerificationConstants.VerificationCodePrefix;

        if (inputCode.StartsWith(prefixWithDash, StringComparison.OrdinalIgnoreCase))
        {
            inputCode = inputCode.Substring(prefixWithDash.Length);
        }
        else if (inputCode.StartsWith(prefixNoDash, StringComparison.OrdinalIgnoreCase))
        {
            inputCode = inputCode.Substring(prefixNoDash.Length);
        }

        if (!string.Equals(inputCode, tutor.Bankverifycode, StringComparison.OrdinalIgnoreCase))
        {
            tutor.Bankverifyattempts++;
            await unitOfWork.SaveChangesAsync();

            var attemptsLeft = _settings.MaxVerificationAttempts - tutor.Bankverifyattempts;
            logger.LogWarning("Verification failed for user {UserId}. Attempts left: {AttemptsLeft}", userId, attemptsLeft);

            return new ConfirmVerifyResponse
            {
                IsVerified = false,
                AttemptsLeft = attemptsLeft,
                ErrorMessage = $"Mã xác minh không đúng. Bạn còn {attemptsLeft} lần thử."
            };
        }

        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        tutor.Isbankverified = true;
        tutor.Bankverifiedat = now;
        tutor.Bankverifystatus = BankVerificationConstants.Statuses.Verified;
        tutor.Bankverifyattempts = 0;

        await unitOfWork.SaveChangesAsync();

        logger.LogInformation("Bank verified successfully for user {UserId}", userId);

        return new ConfirmVerifyResponse
        {
            IsVerified = true,
            VerifiedAt = now,
            AttemptsLeft = _settings.MaxVerificationAttempts,
            Message = "Xác minh tài khoản ngân hàng thành công!"
        };
    }

    public async Task<BankVerificationStatusResponse> GetVerificationStatusAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var tutor = await unitOfWork.TutorRepository.GetTutorProfileByUserIdAsync(userId, cancellationToken)
            ?? throw new TutorProfileNotFoundException();

        var attemptsLeft = Math.Max(0, _settings.MaxVerificationAttempts - tutor.Bankverifyattempts);
        var expiresAt = tutor.Bankverifyrequested?.AddHours(_settings.VerificationCodeExpiryHours);

        return new BankVerificationStatusResponse
        {
            IsVerified = tutor.Isbankverified ?? false,
            IsPending = tutor.Bankverifystatus == BankVerificationConstants.Statuses.PendingDeposit,
            IsReadyToConfirm = tutor.Bankverifystatus == BankVerificationConstants.Statuses.ReadyToConfirm,
            AttemptsLeft = attemptsLeft,
            ExpiresAt = expiresAt,
            Status = tutor.Bankverifystatus ?? BankVerificationConstants.Statuses.None,
            BankName = tutor.Bankname,
            MaskedAccountNumber = MaskAccountNumber(tutor.Bankaccountnumber)
        };
    }

    public async Task<List<BankInfoResponse>> GetBankListAsync(CancellationToken cancellationToken = default)
    {
        if (memoryCache.TryGetValue<List<BankInfoResponse>>(L1_CACHE_KEY, out var cachedBanks))
        {
            logger.LogDebug("Bank list served from L1 cache");
            _ = Task.Run(() => TryBackgroundRefreshAsync(cancellationToken), CancellationToken.None);
            return cachedBanks!;
        }

        var l2Cached = await cache.GetStringAsync(L2_CACHE_KEY, cancellationToken);
        if (!string.IsNullOrEmpty(l2Cached))
        {
            var banks = JsonSerializer.Deserialize<List<BankInfoResponse>>(l2Cached) ?? [];
            SetL1Cache(banks);
            logger.LogDebug("Bank list served from L2 cache, populated L1");
            return banks;
        }

        return await FetchAndCacheBankListAsync(cancellationToken);
    }

    private async Task<List<BankInfoResponse>> FetchAndCacheBankListAsync(CancellationToken cancellationToken)
    {
        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            if (memoryCache.TryGetValue<List<BankInfoResponse>>(L1_CACHE_KEY, out var cachedBanks))
                return cachedBanks!;

            logger.LogInformation("Fetching bank list from VietQR API");

            List<BankInfoResponse> banks;
            try
            {
                banks = await vietQRClient.GetBankListAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to fetch from VietQR API, using fallback");
                banks = GetFallbackBankList();
            }

            SetL1Cache(banks);
            await SetL2CacheAsync(banks, cancellationToken);

            return banks;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private void SetL1Cache(List<BankInfoResponse> banks) =>
        memoryCache.Set(L1_CACHE_KEY, banks, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_vietQRSettings.L1CacheMinutes),
            Priority = CacheItemPriority.High,
            Size = 1
        });

    private async Task SetL2CacheAsync(List<BankInfoResponse> banks, CancellationToken cancellationToken) =>
        await cache.SetStringAsync(L2_CACHE_KEY, JsonSerializer.Serialize(banks),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(_vietQRSettings.L2CacheHours) },
            cancellationToken);

    private async Task TryBackgroundRefreshAsync(CancellationToken cancellationToken)
    {
        var lockKey = $"{REFRESH_LOCK_KEY}:{MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow:yyyyMMddHH}";
        var lockValue = await cache.GetStringAsync(lockKey, cancellationToken);

        if (!string.IsNullOrEmpty(lockValue))
            return;

        try
        {
            await cache.SetStringAsync(
                lockKey,
                "locked",
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) },
                cancellationToken);

            logger.LogInformation("Background refresh started");

            var banks = await vietQRClient.GetBankListAsync(cancellationToken);

            SetL1Cache(banks);
            await SetL2CacheAsync(banks, cancellationToken);

            logger.LogInformation("Background refresh completed");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Background refresh failed (non-critical)");
        }
    }

    private static List<BankInfoResponse> GetFallbackBankList() =>
    [
        new() { Code = "VCB", ShortName = "Vietcombank", FullName = "Ngân hàng TMCP Ngoại thương Việt Nam", Bin = "970436", SupportsInstantTransfer = true },
        new() { Code = "TCB", ShortName = "Techcombank", FullName = "Ngân hàng TMCP Kỹ thương Việt Nam", Bin = "970407", SupportsInstantTransfer = true },
        new() { Code = "MB", ShortName = "MB Bank", FullName = "Ngân hàng TMCP Quân đội", Bin = "970422", SupportsInstantTransfer = true },
        new() { Code = "VIB", ShortName = "VIB", FullName = "Ngân hàng TMCP Quốc tế Việt Nam", Bin = "970441", SupportsInstantTransfer = true },
        new() { Code = "ACB", ShortName = "ACB", FullName = "Ngân hàng TMCP Á Châu", Bin = "970416", SupportsInstantTransfer = true },
        new() { Code = "TPB", ShortName = "TPBank", FullName = "Ngân hàng TMCP Tiên Phong", Bin = "970423", SupportsInstantTransfer = true },
        new() { Code = "STB", ShortName = "Sacombank", FullName = "Ngân hàng TMCP Sài Gòn Thương Tín", Bin = "970403", SupportsInstantTransfer = true },
        new() { Code = "VPB", ShortName = "VPBank", FullName = "Ngân hàng TMCP Việt Nam Thịnh Vượng", Bin = "970432", SupportsInstantTransfer = true },
        new() { Code = "BIDV", ShortName = "BIDV", FullName = "Ngân hàng TMCP Đầu tư và Phát triển Việt Nam", Bin = "970418", SupportsInstantTransfer = true },
        new() { Code = "AGR", ShortName = "Agribank", FullName = "Ngân hàng Nông nghiệp và Phát triển Nông thôn Việt Nam", Bin = "970405", SupportsInstantTransfer = true },
        new() { Code = "OCB", ShortName = "OCB", FullName = "Ngân hàng TMCP Phương Đông", Bin = "970448", SupportsInstantTransfer = true },
        new() { Code = "MSB", ShortName = "MSB", FullName = "Ngân hàng TMCP Hàng Hải", Bin = "970426", SupportsInstantTransfer = true },
        new() { Code = "SHB", ShortName = "SHB", FullName = "Ngân hàng TMCP Sài Gòn - Hà Nội", Bin = "970443", SupportsInstantTransfer = true },
        new() { Code = "VietinBank", ShortName = "VietinBank", FullName = "Ngân hàng TMCP Công thương Việt Nam", Bin = "970415", SupportsInstantTransfer = true }
    ];

    private static void ValidateAccountNumber(string accountNumber)
    {
        if (string.IsNullOrWhiteSpace(accountNumber) || !AccountNumberRegex().IsMatch(accountNumber))
            throw new InvalidBankInfoException(
                BankVerificationConstants.ErrorMessages.InvalidAccountNumber,
                BankVerificationConstants.ErrorCodes.InvalidAccountNumber);
    }

    private async Task CheckVerificationRequestRateLimitAsync(string userId, CancellationToken cancellationToken)
    {
        var key = $"bank-verify-request:{userId}:{MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow:yyyyMMdd}";
        var countStr = await cache.GetStringAsync(key, cancellationToken);
        var count = int.TryParse(countStr, out var c) ? c : 0;

        if (count >= _settings.RateLimitMaxRequests)
            throw new RateLimitExceededException("Bạn đã vượt quá số lần yêu cầu xác thực trong ngày.");
    }

    private async Task IncrementVerificationRequestRateLimitAsync(string userId, CancellationToken cancellationToken)
    {
        var key = $"bank-verify-request:{userId}:{MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow:yyyyMMdd}";
        var countStr = await cache.GetStringAsync(key, cancellationToken);
        var count = int.TryParse(countStr, out var c) ? c : 0;

        await cache.SetStringAsync(key, (count + 1).ToString(),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(_settings.RateLimitWindowHours) },
            cancellationToken);
    }

    private string MaskAccountNumber(string? accountNumber) =>
        AccountMaskingHelper.MaskAccountNumber(accountNumber, _settings);

    private string GenerateVerificationCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var code = new char[_settings.VerificationCodeLength];

        for (var i = 0; i < code.Length; i++)
            code[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];

        return new string(code);
    }
}
