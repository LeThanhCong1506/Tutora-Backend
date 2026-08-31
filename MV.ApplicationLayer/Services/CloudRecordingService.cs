using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.ApplicationLayer.Services.Agora;
using MV.DomainLayer.Configuration;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Agora Cloud Recording — điều phối 3 bước REST API acquire → start → stop (mode = mix).
///
/// Thiết kế:
///   - channel do caller truyền vào (AgoraChannelName.ForSession — channel riêng theo buổi),
///     PHẢI khớp với channel client join thì recorder mới thấy stream.
///   - Recorder join bằng UID số riêng (RecorderUid), token ký bằng AppCertificate hiện có.
///   - Ghi tất cả stream (#allstream#) ghép thành 1 video (mix) → xuất HLS + MP4 lên storage.
/// </summary>
public class CloudRecordingService : ICloudRecordingService
{
    private const string BaseUrl = "https://api.agora.io/v1/apps";

    private readonly HttpClient _http;
    private readonly AgoraSettings _agora;
    private readonly AgoraRecordingSettings _rec;
    private readonly ILogger<CloudRecordingService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public CloudRecordingService(
        HttpClient http,
        IOptions<AgoraSettings> agora,
        IOptions<AgoraRecordingSettings> rec,
        ILogger<CloudRecordingService> logger)
    {
        _http = http;
        _agora = agora.Value;
        _rec = rec.Value;
        _logger = logger;
    }

    public bool Enabled => _rec.Enabled;
    public bool AudioOnlyEnabled => _rec.AudioRecordingEnabled;

    public Task<CloudRecordingHandle> StartAsync(int classSessionId, string channel, CancellationToken ct = default)
        => StartInternalAsync(classSessionId, channel, _rec.RecorderUid, audioOnly: false, ct);

    public Task<CloudRecordingResult> StopAsync(
        int classSessionId, string channel, string resourceId, string sid, CancellationToken ct = default)
        => StopInternalAsync(classSessionId, channel, resourceId, sid, _rec.RecorderUid, ct);

    /// <summary>Recorder audio-only, chạy song song với recorder video (uid khác, resourceId/sid riêng,
    /// lưu ở thư mục S3 riêng "recordings-audio/&lt;id&gt;") — không thay thế recorder video, chỉ để pipeline
    /// AI dùng file audio nhỏ thay vì phải tải nguyên video rồi ffmpeg tách audio.</summary>
    public Task<CloudRecordingHandle> StartAudioAsync(int classSessionId, string channel, CancellationToken ct = default)
        => StartInternalAsync(classSessionId, channel, _rec.AudioRecorderUid, audioOnly: true, ct);

    public Task<CloudRecordingResult> StopAudioAsync(
        int classSessionId, string channel, string resourceId, string sid, CancellationToken ct = default)
        => StopInternalAsync(classSessionId, channel, resourceId, sid, _rec.AudioRecorderUid, ct);

    public async Task<CloudRecordingQueryResult> QueryAsync(string resourceId, string sid, CancellationToken ct = default)
    {
        Validate();
        var url = $"{BaseUrl}/{_agora.AppId}/cloud_recording/resourceid/{Uri.EscapeDataString(resourceId)}/sid/{Uri.EscapeDataString(sid)}/mode/mix/query";
        var query = await GetAsync<QueryResponse>(url, ct);

        var files = query.ServerResponse?.FileList?
            .Select(f => f.FileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToList() ?? new List<string>();

        return new CloudRecordingQueryResult(query.ServerResponse?.Status ?? -1, files);
    }

    // ── Core (dùng chung cho cả recorder video và recorder audio-only) ─────────

    private async Task<CloudRecordingHandle> StartInternalAsync(
        int classSessionId, string channel, uint recorderUid, bool audioOnly, CancellationToken ct)
    {
        Validate();
        var uid = recorderUid.ToString();

        // ① acquire — xin resourceId
        var acquireBody = new
        {
            cname = channel,
            uid,
            clientRequest = new { resourceExpiredHour = 24, scene = 0 }
        };
        var acquire = await PostAsync<AcquireResponse>(
            $"{BaseUrl}/{_agora.AppId}/cloud_recording/acquire", acquireBody, ct);
        var resourceId = acquire.ResourceId
            ?? throw new InvalidOperationException("Agora acquire không trả về resourceId.");

        // ② start — bắt đầu record (mode = mix cho cả 2, chỉ khác streamTypes/transcodingConfig).
        // audioOnly=true bỏ hẳn transcodingConfig (không có video để mã hoá) — dùng streamTypes=0.
        var token = BuildRecorderToken(channel, recorderUid);
        object recordingConfig = audioOnly
            ? new
            {
                channelType = 0,
                streamTypes = 0,                        // 0 = chỉ audio
                maxIdleTime = _rec.MaxIdleTimeSeconds,
                subscribeAudioUids = new[] { "#allstream#" },
            }
            : new
            {
                channelType = 0,                        // 0 = communication (client dùng mode 'rtc')
                streamTypes = 2,                        // 0 audio, 1 video, 2 audio+video
                maxIdleTime = _rec.MaxIdleTimeSeconds,   // tự dừng nếu channel trống quá lâu
                subscribeAudioUids = new[] { "#allstream#" },
                subscribeVideoUids = new[] { "#allstream#" },
                transcodingConfig = new
                {
                    width = _rec.VideoWidth,
                    height = _rec.VideoHeight,
                    fps = _rec.VideoFps,
                    bitrate = _rec.VideoBitrate,
                    mixedVideoLayout = 1,               // 1 = best fit
                }
            };
        var startBody = new
        {
            cname = channel,
            uid,
            clientRequest = new
            {
                token,
                recordingConfig,
                recordingFileConfig = new { avFileType = new[] { "hls", "mp4" } },
                storageConfig = BuildStorageConfig(classSessionId, audioOnly)
            }
        };
        var start = await PostAsync<StartResponse>(
            $"{BaseUrl}/{_agora.AppId}/cloud_recording/resourceid/{resourceId}/mode/mix/start", startBody, ct);
        var sid = start.Sid
            ?? throw new InvalidOperationException("Agora start không trả về sid.");

        _logger.LogInformation(
            "Cloud recording STARTED ({Kind}): session={Session} resourceId={ResourceId} sid={Sid}",
            audioOnly ? "audio-only" : "video", classSessionId, resourceId, sid);
        return new CloudRecordingHandle(resourceId, sid);
    }

    private async Task<CloudRecordingResult> StopInternalAsync(
        int classSessionId, string channel, string resourceId, string sid, uint recorderUid, CancellationToken ct)
    {
        Validate();
        var uid = recorderUid.ToString();

        var stopBody = new
        {
            cname = channel,
            uid,
            clientRequest = new { async_stop = false }
        };
        var stop = await PostAsync<StopResponse>(
            $"{BaseUrl}/{_agora.AppId}/cloud_recording/resourceid/{resourceId}/sid/{sid}/mode/mix/stop", stopBody, ct);

        var files = stop.ServerResponse?.FileList?
            .Select(f => f.FileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToList() ?? new List<string>();

        // Ưu tiên file .mp4 (container mp4 chỉ chứa audio track vẫn nhận .mp4 với recorder audio-only)
        var main = files.FirstOrDefault(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                   ?? files.FirstOrDefault(f => f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase))
                   ?? files.FirstOrDefault();
        string? playbackUrl = null;
        if (!string.IsNullOrWhiteSpace(_rec.PublicUrlBase) && main != null)
            playbackUrl = $"{_rec.PublicUrlBase!.TrimEnd('/')}/{main}";

        _logger.LogInformation(
            "Cloud recording STOPPED: session={Session} files={Count}", classSessionId, files.Count);
        return new CloudRecordingResult(resourceId, sid, files, playbackUrl);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private object BuildStorageConfig(int classSessionId, bool audioOnly)
    {
        var cfg = new Dictionary<string, object?>
        {
            ["vendor"] = _rec.StorageVendor,
            ["region"] = _rec.StorageRegion,
            ["bucket"] = _rec.StorageBucket,
            ["accessKey"] = _rec.StorageAccessKey,
            ["secretKey"] = _rec.StorageSecretKey,
            // Video: recordings/<id>/... — Audio-only: recordings-audio/<id>/... (thư mục riêng, không
            // lẫn với video, để job relay dễ phân biệt việc cần làm gì với từng loại).
            ["fileNamePrefix"] = audioOnly
                ? new[] { "recordings-audio", classSessionId.ToString() }
                : new[] { "recordings", classSessionId.ToString() },
        };
        // S3-compatible (Backblaze B2, R2...) cần endpoint tùy chỉnh.
        // Agora yêu cầu "domain name" — KHÔNG kèm http/https.
        if (!string.IsNullOrWhiteSpace(_rec.StorageEndpoint))
        {
            var endpoint = _rec.StorageEndpoint!
                .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
                .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
                .TrimEnd('/');
            cfg["extensionParams"] = new { endpoint };
        }
        return cfg;
    }

    private string BuildRecorderToken(string channel, uint recorderUid)
    {
        var issueTs = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Span<byte> saltBytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(saltBytes);
        var salt = BitConverter.ToUInt32(saltBytes);
        if (salt == 0) salt = 1u;

        return RtcTokenBuilder2.BuildTokenWithUid(
            _agora.AppId,
            _agora.AppCertificate,
            channel,
            recorderUid,
            RtcTokenBuilder2.RolePublisher,
            tokenExpire: _rec.TokenExpireSeconds,
            privilegeExpire: _rec.TokenExpireSeconds,
            issueTs: issueTs,
            salt: salt);
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(_agora.AppId) || string.IsNullOrWhiteSpace(_agora.AppCertificate))
            throw new InvalidOperationException("Agora chưa cấu hình AppId/AppCertificate.");
        if (string.IsNullOrWhiteSpace(_rec.CustomerId) || string.IsNullOrWhiteSpace(_rec.CustomerSecret))
            throw new InvalidOperationException("AgoraRecording chưa cấu hình CustomerId/CustomerSecret.");
        if (string.IsNullOrWhiteSpace(_rec.StorageBucket))
            throw new InvalidOperationException("AgoraRecording chưa cấu hình StorageBucket.");
    }

    private async Task<T> PostAsync<T>(string url, object body, CancellationToken ct)
    {
        // QUAN TRỌNG: serialize theo runtime type (body.GetType()) để anonymous object
        // không bị serialize rỗng khi biến khai báo kiểu object.
        var json = JsonSerializer.Serialize(body, body.GetType(), JsonOpts);

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_rec.CustomerId}:{_rec.CustomerSecret}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        using var resp = await _http.SendAsync(req, ct);
        var respJson = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("Agora recording API {Url} lỗi {Status}: {Body}", url, (int)resp.StatusCode, respJson);
            throw new InvalidOperationException($"Agora recording API lỗi {(int)resp.StatusCode}: {respJson}");
        }

        return JsonSerializer.Deserialize<T>(respJson, JsonOpts)
            ?? throw new InvalidOperationException($"Không parse được phản hồi Agora: {respJson}");
    }

    private async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_rec.CustomerId}:{_rec.CustomerSecret}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        using var resp = await _http.SendAsync(req, ct);
        var respJson = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("Agora recording API {Url} lỗi {Status}: {Body}", url, (int)resp.StatusCode, respJson);
            throw new InvalidOperationException($"Agora recording API lỗi {(int)resp.StatusCode}: {respJson}");
        }

        return JsonSerializer.Deserialize<T>(respJson, JsonOpts)
            ?? throw new InvalidOperationException($"Không parse được phản hồi Agora: {respJson}");
    }

    // ── DTO phản hồi Agora ──────────────────────────────────────────────────
    private sealed record AcquireResponse(
        [property: JsonPropertyName("resourceId")] string? ResourceId);

    private sealed record StartResponse(
        [property: JsonPropertyName("sid")] string? Sid,
        [property: JsonPropertyName("resourceId")] string? ResourceId);

    private sealed record StopResponse(
        [property: JsonPropertyName("resourceId")] string? ResourceId,
        [property: JsonPropertyName("sid")] string? Sid,
        [property: JsonPropertyName("serverResponse")] StopServerResponse? ServerResponse);

    private sealed record StopServerResponse(
        [property: JsonPropertyName("fileList")] List<StopFile>? FileList);

    private sealed record StopFile(
        [property: JsonPropertyName("fileName")] string? FileName);

    private sealed record QueryResponse(
        [property: JsonPropertyName("resourceId")] string? ResourceId,
        [property: JsonPropertyName("sid")] string? Sid,
        [property: JsonPropertyName("serverResponse")] QueryServerResponse? ServerResponse);

    private sealed record QueryServerResponse(
        [property: JsonPropertyName("status")] int? Status,
        [property: JsonPropertyName("fileList")] List<StopFile>? FileList);
}
