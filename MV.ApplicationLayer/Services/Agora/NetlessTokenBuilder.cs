using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MV.ApplicationLayer.Services.Agora;

/// <summary>
/// Sinh token cho Agora Interactive Whiteboard (Netless) — port từ sample code chính thức
/// netless-io/netless-token (bản C#). Tạo SDK token + Room token, ký HMAC-SHA256.
///
/// Định dạng token: PREFIX + base64url( querystring các trường + sig ).
/// sig = hex(HMACSHA256(secretKey, JSON của các trường đã sort)).
/// </summary>
internal static class NetlessTokenBuilder
{
    /// <summary>Toàn quyền (admin) — tutor.</summary>
    public const string RoleAdmin = "0";
    /// <summary>Được vẽ/ghi (writer) — học viên.</summary>
    public const string RoleWriter = "1";
    /// <summary>Chỉ xem (reader).</summary>
    public const string RoleReader = "2";

    private const string SdkPrefix = "NETLESSSDK_";
    private const string RoomPrefix = "NETLESSROOM_";

    /// <summary>SDK token — để gọi REST API Netless (tạo phòng...). lifespan tính bằng MILI-GIÂY.</summary>
    public static string SdkToken(string accessKey, string secretKey, long lifespanMs, string role)
    {
        var content = new SortedDictionary<string, string> { ["role"] = role };
        return CreateToken(SdkPrefix, accessKey, secretKey, lifespanMs, content);
    }

    /// <summary>Room token — cho client join một phòng cụ thể. lifespan tính bằng MILI-GIÂY.</summary>
    public static string RoomToken(string accessKey, string secretKey, long lifespanMs, string role, string uuid)
    {
        var content = new SortedDictionary<string, string> { ["role"] = role, ["uuid"] = uuid };
        return CreateToken(RoomPrefix, accessKey, secretKey, lifespanMs, content);
    }

    private static string CreateToken(
        string prefix, string accessKey, string secretKey, long lifespanMs, IDictionary<string, string> content)
    {
        var m = new SortedDictionary<string, string>
        {
            ["ak"] = accessKey,
            ["nonce"] = Guid.NewGuid().ToString(),
        };
        foreach (var kv in content) m.Add(kv.Key, kv.Value);

        if (lifespanMs > 0)
            m.Add("expireAt", (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + lifespanMs).ToString());

        // Ký HMAC-SHA256 trên JSON của map đã sort (chưa gồm sig)
        var information = JsonSerializer.Serialize(m);
        using (var digest = new HMACSHA256(Encoding.ASCII.GetBytes(secretKey)))
        {
            var hmac = digest.ComputeHash(Encoding.ASCII.GetBytes(information));
            m.Add("sig", ToHex(hmac));
        }

        return prefix + ToBase64Url(Querify(m));
    }

    private static string ToBase64Url(string s) =>
        Convert.ToBase64String(Encoding.ASCII.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.AppendFormat("{0:x2}", b);
        return sb.ToString();
    }

    private static string Querify(IDictionary<string, string> map)
    {
        var parts = new List<string>();
        foreach (var e in map)
            parts.Add($"{Uri.EscapeDataString(e.Key)}={Uri.EscapeDataString(e.Value)}");
        return string.Join('&', parts);
    }
}
