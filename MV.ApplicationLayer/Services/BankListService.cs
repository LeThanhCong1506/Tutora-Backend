using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Interfaces;
using MV.DomainLayer.Settings;
using System.Text.Json;

namespace MV.ApplicationLayer.Services;

public class BankListService(
    IVietQRClient vietQRClient,
    IMemoryCache memoryCache,
    IDistributedCache cache,
    IOptions<VietQRSettings> vietQRSettings,
    ILogger<BankListService> logger) : IBankListService
{
    private readonly VietQRSettings _vietQRSettings = vietQRSettings.Value;

    private static readonly SemaphoreSlim _cacheLock = new(1, 1);
    private const string L1_CACHE_KEY = "bank-list:l1";
    private const string L2_CACHE_KEY = "bank-list:l2";
    private const string REFRESH_LOCK_KEY = "bank-list:refresh-lock";

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
}
