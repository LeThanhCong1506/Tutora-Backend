using Microsoft.Extensions.Options;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace MV.InfrastructureLayer.Services
{
    /// <summary>
    /// AES-256-CBC với fixed IV — mã hóa deterministic.
    /// Cùng plaintext luôn cho cùng ciphertext → cho phép DB equality check (IsIdentityNumberUniqueAsync).
    /// Key + IV lưu trong appsettings, KHÔNG lưu trong DB.
    /// </summary>
    public class AesEncryptionService : IEncryptionService
    {
        private readonly byte[] _key;
        private readonly byte[] _iv;

        public AesEncryptionService(IOptions<EncryptionSettings> options)
        {
            var settings = options.Value;

            if (string.IsNullOrWhiteSpace(settings.Key) || string.IsNullOrWhiteSpace(settings.Iv))
                throw new InvalidOperationException("Encryption:Key và Encryption:Iv phải được cấu hình trong appsettings.");

            _key = Convert.FromBase64String(settings.Key);
            _iv  = Convert.FromBase64String(settings.Iv);

            if (_key.Length != 32)
                throw new InvalidOperationException("Encryption:Key phải là 32 bytes (AES-256).");
            if (_iv.Length != 16)
                throw new InvalidOperationException("Encryption:Iv phải là 16 bytes (AES block size).");
        }

        public string Encrypt(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext)) return plaintext;

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
            if (string.IsNullOrEmpty(ciphertext)) return ciphertext;

            try
            {
                var bytes = Convert.FromBase64String(ciphertext);

                using var aes = Aes.Create();
                aes.Key     = _key;
                aes.IV      = _iv;
                aes.Mode    = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var decryptor = aes.CreateDecryptor();
                var decrypted = decryptor.TransformFinalBlock(bytes, 0, bytes.Length);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                // Dữ liệu cũ chưa mã hóa trong DB → trả về nguyên bản
                return ciphertext;
            }
        }
    }
}
