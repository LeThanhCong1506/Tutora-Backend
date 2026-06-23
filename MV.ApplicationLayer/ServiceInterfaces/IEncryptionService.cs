namespace MV.ApplicationLayer.ServiceInterfaces
{
    public interface IEncryptionService
    {
        /// <summary>Mã hóa AES-256-CBC — cùng input luôn cho cùng output (deterministic).</summary>
        string Encrypt(string plaintext);

        /// <summary>Giải mã AES-256-CBC. Trả về nguyên bản nếu input không phải ciphertext hợp lệ.</summary>
        string Decrypt(string ciphertext);
    }
}
