using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using System.Security.Cryptography;
using System.Text;

namespace MV.InfrastructureLayer.Services
{
    /// <summary>
    /// AES-256-CBC encryption for sensitive PII fields (identity_number, ekyc_raw_data).
    /// Key and IV are read from appsettings Encryption:Key / Encryption:Iv (Base64-encoded).
    /// Fixed IV → deterministic encryption, enabling exact-match DB queries (uniqueness checks).
    /// </summary>
    public class AesEncryptionService : IEncryptionService
    {
        private readonly byte[] _key;
        private readonly byte[] _iv;
        private readonly ILogger<AesEncryptionService> _logger;

        public AesEncryptionService(IConfiguration config, ILogger<AesEncryptionService> logger)
        {
            _logger = logger;
            var keyB64 = config["Encryption:Key"]
                ?? throw new InvalidOperationException("Encryption:Key not configured.");
            var ivB64 = config["Encryption:Iv"]
                ?? throw new InvalidOperationException("Encryption:Iv not configured.");

            _key = Convert.FromBase64String(keyB64);
            _iv  = Convert.FromBase64String(ivB64);

            if (_key.Length != 32)
                throw new InvalidOperationException("Encryption:Key must be 32 bytes (AES-256).");
            if (_iv.Length != 16)
                throw new InvalidOperationException("Encryption:Iv must be 16 bytes.");
        }

        public string Encrypt(string plaintext)
        {
            using var aes = Aes.Create();
            aes.Key     = _key;
            aes.IV      = _iv;
            aes.Mode    = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var encryptor = aes.CreateEncryptor();
            var bytes = Encoding.UTF8.GetBytes(plaintext);
            var encrypted = encryptor.TransformFinalBlock(bytes, 0, bytes.Length);
            return Convert.ToBase64String(encrypted);
        }

        public string? Decrypt(string? ciphertext)
        {
            if (string.IsNullOrWhiteSpace(ciphertext))
                return null;

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
            catch (Exception ex)
            {
                // Pre-migration record: value is plain text, not encrypted yet.
                _logger.LogWarning(ex, "Decrypt failed — value may be pre-migration plaintext.");
                return ciphertext;
            }
        }
    }
}
