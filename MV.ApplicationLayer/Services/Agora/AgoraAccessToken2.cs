using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace MV.ApplicationLayer.Services.Agora;

/// <summary>
/// Buffer đóng gói dữ liệu nhị phân theo định dạng little-endian mà Agora AccessToken2 yêu cầu.
/// Tương đương ByteBuf trong bộ AgoraDynamicKey chính thức.
/// </summary>
internal sealed class ByteBuf
{
    private readonly MemoryStream _stream = new();

    public byte[] AsBytes() => _stream.ToArray();

    /// <summary>Ghi uint16 little-endian (2 byte).</summary>
    public ByteBuf PutUint16(ushort value)
    {
        _stream.WriteByte((byte)(value & 0xFF));
        _stream.WriteByte((byte)((value >> 8) & 0xFF));
        return this;
    }

    /// <summary>Ghi uint32 little-endian (4 byte).</summary>
    public ByteBuf PutUint32(uint value)
    {
        _stream.WriteByte((byte)(value & 0xFF));
        _stream.WriteByte((byte)((value >> 8) & 0xFF));
        _stream.WriteByte((byte)((value >> 16) & 0xFF));
        _stream.WriteByte((byte)((value >> 24) & 0xFF));
        return this;
    }

    /// <summary>Ghi mảng byte có tiền tố độ dài uint16 (tương đương pack_string của Agora).</summary>
    public ByteBuf PutBytes(byte[] value)
    {
        PutUint16((ushort)value.Length);
        _stream.Write(value, 0, value.Length);
        return this;
    }

    /// <summary>Ghi chuỗi UTF-8 có tiền tố độ dài uint16.</summary>
    public ByteBuf PutString(string value) => PutBytes(Encoding.UTF8.GetBytes(value));

    /// <summary>Ghi thẳng mảng byte, không kèm tiền tố độ dài.</summary>
    public ByteBuf PutRawBytes(byte[] value)
    {
        _stream.Write(value, 0, value.Length);
        return this;
    }

    /// <summary>Ghi map&lt;uint16,uint32&gt; theo thứ tự khóa tăng dần.</summary>
    public ByteBuf PutTreeMap(SortedDictionary<ushort, uint> map)
    {
        PutUint16((ushort)map.Count);
        foreach (var kv in map)
        {
            PutUint16(kv.Key);
            PutUint32(kv.Value);
        }
        return this;
    }

    /// <summary>Đóng gói uint32 thành 4 byte little-endian thô (dùng làm khóa HMAC).</summary>
    public static byte[] PackUint32(uint value) =>
        new[]
        {
            (byte)(value & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 24) & 0xFF)
        };
}

/// <summary>
/// Một "service" trong AccessToken2. Mỗi service có type và tập privilege (privilege → expire tương đối).
/// </summary>
internal abstract class Service
{
    private readonly ushort _type;
    protected readonly SortedDictionary<ushort, uint> Privileges = new();

    protected Service(ushort type) => _type = type;

    public ushort Type => _type;

    public void AddPrivilege(ushort privilege, uint expire) => Privileges[privilege] = expire;

    public virtual void Pack(ByteBuf buf)
    {
        buf.PutUint16(_type);
        buf.PutTreeMap(Privileges);
    }
}

/// <summary>Service RTC (video/audio call) trong AccessToken2.</summary>
internal sealed class ServiceRtc : Service
{
    public const ushort ServiceType = 1;

    public const ushort PrivilegeJoinChannel = 1;
    public const ushort PrivilegePublishAudioStream = 2;
    public const ushort PrivilegePublishVideoStream = 3;
    public const ushort PrivilegePublishDataStream = 4;

    private readonly string _channelName;
    private readonly string _uid;

    public ServiceRtc(string channelName, string uid) : base(ServiceType)
    {
        _channelName = channelName;
        _uid = uid;
    }

    public override void Pack(ByteBuf buf)
    {
        base.Pack(buf);
        buf.PutString(_channelName);
        buf.PutString(_uid);
    }
}

/// <summary>Service RTM (Real-Time Messaging / chat) trong AccessToken2.</summary>
internal sealed class ServiceRtm : Service
{
    public const ushort ServiceType = 2;

    public const ushort PrivilegeLogin = 1;

    private readonly string _userId;

    public ServiceRtm(string userId) : base(ServiceType)
    {
        _userId = userId;
    }

    public override void Pack(ByteBuf buf)
    {
        base.Pack(buf);
        buf.PutString(_userId);
    }
}

/// <summary>
/// Sinh Agora AccessToken2 (version "007") theo thuật toán chính thức của Agora.
///
/// Định dạng: "007" + Base64( zlib( pack_string(signature) + signing_info ) )
///   signing_info = pack_string(appId) + uint32(issueTs) + uint32(expire) + uint32(salt)
///                  + uint16(serviceCount) + [service.Pack() ...]
///   signing key  = HMAC-SHA256( salt , HMAC-SHA256( issueTs , appCertificate ) )
///   signature    = HMAC-SHA256( signing key , signing_info )
///
/// Ref: https://github.com/AgoraIO/Tools/tree/master/DynamicKey/AgoraDynamicKey/csharp
/// </summary>
internal sealed class AccessToken2
{
    public const string Version = "007";

    private readonly string _appId;
    private readonly string _appCertificate;
    private readonly uint _issueTs;
    private readonly uint _expire;
    private readonly uint _salt;
    private readonly SortedDictionary<ushort, Service> _services = new();

    public AccessToken2(string appId, string appCertificate, uint issueTs, uint expire, uint salt)
    {
        _appId = appId;
        _appCertificate = appCertificate;
        _issueTs = issueTs;
        _expire = expire;
        _salt = salt;
    }

    public void AddService(Service service) => _services[service.Type] = service;

    public string Build()
    {
        // signing_info
        var signingInfoBuf = new ByteBuf()
            .PutString(_appId)
            .PutUint32(_issueTs)
            .PutUint32(_expire)
            .PutUint32(_salt)
            .PutUint16((ushort)_services.Count);
        foreach (var kv in _services)
        {
            kv.Value.Pack(signingInfoBuf);
        }
        byte[] signingInfo = signingInfoBuf.AsBytes();

        // signature = HMAC(signingKey, signing_info)
        byte[] signingKey = GenerateSigningKey();
        byte[] signature = HmacSha256(signingKey, signingInfo);

        // content = pack_string(signature) + signing_info (thô)
        byte[] content = new ByteBuf()
            .PutBytes(signature)
            .PutRawBytes(signingInfo)
            .AsBytes();

        byte[] compressed = Compress(content);
        return Version + Convert.ToBase64String(compressed);
    }

    private byte[] GenerateSigningKey()
    {
        byte[] s1 = HmacSha256(ByteBuf.PackUint32(_issueTs), Encoding.UTF8.GetBytes(_appCertificate));
        byte[] s2 = HmacSha256(ByteBuf.PackUint32(_salt), s1);
        return s2;
    }

    private static byte[] HmacSha256(byte[] key, byte[] message)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(message);
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionMode.Compress, leaveOpen: true))
        {
            zlib.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }
}
