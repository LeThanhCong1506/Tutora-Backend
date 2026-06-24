namespace MV.DomainLayer.Configuration;

public class EncryptionSettings
{
    public string Key { get; set; } = string.Empty; // 32 bytes, Base64
    public string Iv  { get; set; } = string.Empty; // 16 bytes, Base64
}
