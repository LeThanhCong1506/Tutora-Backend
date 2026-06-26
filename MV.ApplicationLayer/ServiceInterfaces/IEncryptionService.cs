namespace MV.ApplicationLayer.ServiceInterfaces
{
    public interface IEncryptionService
    {
        /// <summary>
        /// AES-256-CBC encrypt. Returns Base64 ciphertext.
        /// </summary>
        string Encrypt(string plaintext);

        /// <summary>
        /// AES-256-CBC decrypt. Returns null if input is null/empty or decryption fails.
        /// </summary>
        string? Decrypt(string? ciphertext);
    }
}
