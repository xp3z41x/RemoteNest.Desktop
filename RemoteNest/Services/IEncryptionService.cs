namespace RemoteNest.Services;

public interface IEncryptionService
{
    /// <summary>Encrypts a plain-text password using DPAPI (CurrentUser scope).</summary>
    string Encrypt(string plainText);

    /// <summary>Decrypts a DPAPI-encrypted password back to plain text.</summary>
    string Decrypt(string encryptedText);
}
