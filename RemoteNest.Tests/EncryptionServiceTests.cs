using FluentAssertions;
using RemoteNest.Services;
using Xunit;

namespace RemoteNest.Tests;

public class EncryptionServiceTests
{
    [Fact]
    public void Encrypt_Then_Decrypt_Returns_Original_Plaintext()
    {
        var svc = new EncryptionService();

        var encrypted = svc.Encrypt("correct horse battery staple");
        var decrypted = svc.Decrypt(encrypted);

        decrypted.Should().Be("correct horse battery staple");
        encrypted.Should().NotBe("correct horse battery staple");
    }

    [Fact]
    public void Encrypt_Empty_Returns_Empty()
    {
        var svc = new EncryptionService();

        svc.Encrypt("").Should().Be("");
        svc.Decrypt("").Should().Be("");
    }

    [Fact]
    public void Encrypt_Unicode_RoundTrips()
    {
        var svc = new EncryptionService();
        const string unicode = "senha-日本語-🔐-ção";

        var encrypted = svc.Encrypt(unicode);
        svc.Decrypt(encrypted).Should().Be(unicode);
    }

    [Fact]
    public void Decrypt_Invalid_Base64_Returns_Empty_Without_Throwing()
    {
        var svc = new EncryptionService();

        svc.Decrypt("not-valid-base64!!!!").Should().Be("");
    }

    [Fact]
    public void Decrypt_Tampered_Ciphertext_Returns_Empty_Without_Throwing()
    {
        var svc = new EncryptionService();
        var encrypted = svc.Encrypt("secret");

        // Flip a byte in the middle of the ciphertext — DPAPI detects tamper.
        var bytes = Convert.FromBase64String(encrypted);
        bytes[bytes.Length / 2] ^= 0xFF;
        var tampered = Convert.ToBase64String(bytes);

        svc.Decrypt(tampered).Should().Be("");
    }
}
