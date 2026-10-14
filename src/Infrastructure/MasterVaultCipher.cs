using System.Buffers.Binary;
using System.Security.Cryptography;
using ArchivePassword.Core;

namespace ArchivePassword.Infrastructure;

// APV2 authenticates its version, fixed KDF parameters, salt and fresh nonce.
internal static class MasterVaultCipher
{
    internal static ReadOnlySpan<byte> Magic => "APV2"u8;
    private const int Header = 40, Tag = 16, Iterations = 600_000;
    internal const int MaxBytes = 16 * 1024 * 1024;
    internal static bool IsProtected(ReadOnlySpan<byte> bytes) => bytes.StartsWith(Magic);
    internal static void ValidatePassword(string password)
    {
        if (password.Length < 12 || password.Length > 16_384) throw new VaultException("master_password_length");
    }
    internal static byte[] Salt(byte[] bytes)
    {
        if (bytes.Length < Header + Tag || bytes.Length > MaxBytes || !IsProtected(bytes)
            || BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4)) != 2
            || BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, 4)) != Iterations)
            throw new VaultException("invalid_protected_vault");
        return bytes.AsSpan(12, 16).ToArray();
    }
    internal static byte[] Derive(string password, byte[] salt) => Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
    internal static byte[] Encrypt(byte[] plaintext, byte[] key, byte[] salt)
    {
        if (plaintext.Length > MaxBytes - Header - Tag) throw new VaultException("protected_vault_too_large");
        var bytes = new byte[Header + Tag + plaintext.Length]; Magic.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4), Iterations);
        salt.CopyTo(bytes, 12); RandomNumberGenerator.Fill(bytes.AsSpan(28, 12));
        using var aes = new AesGcm(key, Tag);
        aes.Encrypt(bytes.AsSpan(28, 12), plaintext, bytes.AsSpan(Header + Tag), bytes.AsSpan(Header, Tag), bytes.AsSpan(0, Header));
        return bytes;
    }
    internal static byte[] Decrypt(byte[] bytes, byte[] key, byte[] salt)
    {
        var stored = Salt(bytes);
        if (!stored.AsSpan().SequenceEqual(salt)) throw new VaultException("vault_locked");
        var plaintext = new byte[bytes.Length - Header - Tag];
        try
        {
            using var aes = new AesGcm(key, Tag);
            aes.Decrypt(bytes.AsSpan(28, 12), bytes.AsSpan(Header + Tag), bytes.AsSpan(Header, Tag), plaintext, bytes.AsSpan(0, Header));
            return plaintext;
        }
        catch (CryptographicException) { CryptographicOperations.ZeroMemory(plaintext); throw new VaultException("master_password_or_damage"); }
    }
}
