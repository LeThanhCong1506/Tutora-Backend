namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IEncryptionService
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}
