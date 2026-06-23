namespace MV.DomainLayer.Configuration
{
    public class EncryptionSettings
    {
        /// <summary>AES-256 key — 32 bytes encoded as Base64.</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>AES-256-CBC IV — 16 bytes encoded as Base64. Fixed per deployment.</summary>
        public string Iv { get; set; } = string.Empty;
    }
}
