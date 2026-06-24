using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Configuration;

namespace MV.InfrastructureLayer.Services;

public class AesEncryptionService : IEncryptionService
{
    private readonly byte[] _key;
    private readonly byte[] _iv;

    public AesEncryptionService(IOptions<EncryptionSettings> options)
    {
        var s = options.Value;
        _key = Convert.FromBase64String(s.Key);
        _iv  = Convert.FromBase64String(s.Iv);
        if (_key.Length != 32) throw new InvalidOperationException("Encryption key phải là 32 bytes (256-bit).");
        if (_iv.Length  != 16) throw new InvalidOperationException("Encryption IV phải là 16 bytes.");
    }

    public string Encrypt(string plaintext)
    {
        using var aes = Aes.Create();
        aes.Key     = _key;
        aes.IV      = _iv;
        aes.Mode    = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var bytes     = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = encryptor.TransformFinalBlock(bytes, 0, bytes.Length);
        return Convert.ToBase64String(encrypted);
    }

    public string Decrypt(string ciphertext)
    {
        try
        {
            using var aes = Aes.Create();
            aes.Key     = _key;
            aes.IV      = _iv;
            aes.Mode    = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            var bytes     = Convert.FromBase64String(ciphertext);
            var decrypted = decryptor.TransformFinalBlock(bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            // Backward compat: nếu DB còn plain text cũ thì trả nguyên
            return ciphertext;
        }
    }
}
