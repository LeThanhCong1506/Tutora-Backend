using System.Text.Json;
using Microsoft.Extensions.Logging;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Interfaces;

namespace MV.InfrastructureLayer.ExternalServices;

public class VietQRClient : IVietQRClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<VietQRClient> _logger;

    public VietQRClient(IHttpClientFactory httpClientFactory, ILogger<VietQRClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }
    public async Task<List<BankInfoResponse>> GetBankListAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(ServiceKeys.HttpClients.VietQR);
            var response = await client.GetAsync("/v2/banks", cancellationToken);

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<VietQRResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result?.Data == null)
            {
                _logger.LogWarning("VietQR API returned null data");
                throw new ExternalApiException("VietQR API returned invalid response");
            }

            var banks = result.Data
                .Where(b => !string.IsNullOrEmpty(b.Code))
                .Select(b => new BankInfoResponse
                {
                    Code = b.Code!,
                    ShortName = b.ShortName ?? b.Code!,
                    FullName = b.Name ?? b.ShortName ?? b.Code!,
                    LogoUrl = b.Logo,
                    Bin = b.Bin,
                    SupportsInstantTransfer = true
                })
                .ToList();

            _logger.LogInformation("Fetched {Count} banks from VietQR API", banks.Count);
            return banks;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "VietQR API HTTP error");
            throw new ExternalApiException("VietQR API unavailable", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "VietQR API timeout");
            throw new ExternalApiException("VietQR API timeout", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "VietQR API JSON parsing error");
            throw new ExternalApiException("VietQR API returned invalid JSON", ex);
        }
    }

    private record VietQRResponse(List<VietQRBank>? Data);
    private record VietQRBank(string? Code, string? Name, string? ShortName, string? Logo, string? Bin);
}
