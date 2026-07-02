using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SiNet.Application.Configuration;

namespace SiNet.Infrastructure.Secrets;

/// <summary>
/// Portable encrypted secrets file (AES-256-CBC + PBKDF2). Compatible with legacy <c>SiNet.secrets</c> format.
/// File: [4B magic "SNET"][4B version][16B salt][16B IV][4B length][encrypted JSON payload]
/// </summary>
internal static class SecretProvisioningFileService
{
    private static readonly byte[] Magic = "SNET"u8.ToArray();
    private const int FileVersion = 1;
    private const int SaltSize = 16;
    private const int IvSize = 16;
    private const int KeySize = 32;
    private const int Pbkdf2Iterations = 100_000;

    public static int ExportToFile(ISecretVaultStore vault, string filePath, string password)
    {
        var secrets = new Dictionary<string, string>();
        foreach (var key in SecretCatalog.AllKeys)
        {
            var value = vault.GetSecret(key);
            if (!string.IsNullOrEmpty(value))
            {
                secrets[key] = value;
            }
        }

        if (secrets.Count == 0)
        {
            throw new InvalidOperationException("אין מפתחות מוגדרים ב-Vault לייצוא.");
        }

        return WriteEncryptedDictionary(secrets, filePath, password);
    }

    /// <summary>
    /// Writes an encrypted provisioning file from an arbitrary key dictionary (used by tests and legacy imports).
    /// </summary>
    internal static int WriteEncryptedDictionary(
        IReadOnlyDictionary<string, string> secrets,
        string filePath,
        string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (secrets.Count == 0)
        {
            throw new InvalidOperationException("אין מפתחות לייצוא.");
        }

        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(secrets);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);

        var aesKey = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            KeySize);

        byte[] encryptedPayload;
        byte[] iv;

        using (var aes = Aes.Create())
        {
            aes.KeySize = KeySize * 8;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = aesKey;
            aes.GenerateIV();
            iv = aes.IV;

            using var encryptor = aes.CreateEncryptor();
            encryptedPayload = encryptor.TransformFinalBlock(jsonBytes, 0, jsonBytes.Length);
        }

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(fs);
        writer.Write(Magic);
        writer.Write(FileVersion);
        writer.Write(salt);
        writer.Write(iv);
        writer.Write(encryptedPayload.Length);
        writer.Write(encryptedPayload);

        return secrets.Count;
    }

    public static IReadOnlyDictionary<string, string> DecryptSecrets(string filePath, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("קובץ ההגדרות לא נמצא.", filePath);
        }

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(fs);

        var magic = reader.ReadBytes(4);
        if (!magic.AsSpan().SequenceEqual(Magic))
        {
            throw new InvalidDataException("הקובץ אינו קובץ הגדרות SiNet תקין.");
        }

        var version = reader.ReadInt32();
        if (version != FileVersion)
        {
            throw new InvalidDataException($"גרסת קובץ לא נתמכת: {version}");
        }

        var salt = reader.ReadBytes(SaltSize);
        var iv = reader.ReadBytes(IvSize);
        var encryptedLength = reader.ReadInt32();
        var encryptedPayload = reader.ReadBytes(encryptedLength);

        var aesKey = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            KeySize);

        byte[] jsonBytes;
        try
        {
            using var aes = Aes.Create();
            aes.KeySize = KeySize * 8;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = aesKey;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            jsonBytes = decryptor.TransformFinalBlock(encryptedPayload, 0, encryptedPayload.Length);
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("סיסמה שגויה או קובץ פגום.");
        }

        var secrets = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonBytes);
        if (secrets is null || secrets.Count == 0)
        {
            throw new InvalidDataException("הקובץ אינו מכיל מפתחות.");
        }

        return secrets;
    }

    public static bool IsEncryptedProvisioningFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < 8)
            {
                return false;
            }

            Span<byte> header = stackalloc byte[4];
            fs.ReadExactly(header);
            return header.SequenceEqual(Magic);
        }
        catch
        {
            return false;
        }
    }
}
