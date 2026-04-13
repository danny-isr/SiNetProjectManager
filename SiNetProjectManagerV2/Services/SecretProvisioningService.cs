using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using SiNetSQL.Services;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Encrypts all vault secrets into a portable file (AES-256-CBC + PBKDF2)
/// and imports them back into Windows Credential Manager on another machine.
/// File format: [4B magic "SNET"][4B version][16B salt][16B IV][encrypted JSON payload]
/// </summary>
public static class SecretProvisioningService
{
    private static readonly byte[] _magic = "SNET"u8.ToArray();
    private const int FileVersion = 1;
    private const int SaltSize = 16;
    private const int IvSize = 16;
    private const int KeySize = 32; // AES-256
    private const int Pbkdf2Iterations = 100_000;

    /// <summary>
    /// Exports all configured vault secrets to an encrypted file.
    /// Only secrets that exist in the vault are included.
    /// </summary>
    /// <param name="filePath">Destination file path (e.g., SiNet.secrets).</param>
    /// <param name="password">User-chosen password for PBKDF2 key derivation.</param>
    /// <returns>Number of secrets exported.</returns>
    public static int ExportToFile(string filePath, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        // Collect all existing secrets from the vault
        var secrets = new Dictionary<string, string>();
        foreach (var key in SecretKeys.All)
        {
            var value = CredentialVaultService.GetSecret(key);
            if (!string.IsNullOrEmpty(value))
                secrets[key] = value;
        }

        if (secrets.Count == 0)
            throw new InvalidOperationException("אין מפתחות מוגדרים ב-Vault לייצוא.");

        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(secrets);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);

        using var deriveBytes = new Rfc2898DeriveBytes(
            Encoding.UTF8.GetBytes(password), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256);
        var aesKey = deriveBytes.GetBytes(KeySize);

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

        // Write file: [magic][version][salt][iv][encrypted]
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(fs);
        writer.Write(_magic);
        writer.Write(FileVersion);
        writer.Write(salt);
        writer.Write(iv);
        writer.Write(encryptedPayload.Length);
        writer.Write(encryptedPayload);

        return secrets.Count;
    }

    /// <summary>
    /// Imports secrets from an encrypted provisioning file into Windows Credential Manager.
    /// </summary>
    /// <param name="filePath">Path to the encrypted secrets file.</param>
    /// <param name="password">Password used during export.</param>
    /// <returns>Number of secrets imported.</returns>
    public static int ImportFromFile(string filePath, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (!File.Exists(filePath))
            throw new FileNotFoundException("קובץ ההגדרות לא נמצא.", filePath);

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(fs);

        // Validate magic header
        var magic = reader.ReadBytes(4);
        if (!magic.AsSpan().SequenceEqual(_magic))
            throw new InvalidDataException("הקובץ אינו קובץ הגדרות SiNet תקין.");

        var version = reader.ReadInt32();
        if (version != FileVersion)
            throw new InvalidDataException($"גרסת קובץ לא נתמכת: {version}");

        var salt = reader.ReadBytes(SaltSize);
        var iv = reader.ReadBytes(IvSize);
        var encryptedLength = reader.ReadInt32();
        var encryptedPayload = reader.ReadBytes(encryptedLength);

        // Derive key from password
        using var deriveBytes = new Rfc2898DeriveBytes(
            Encoding.UTF8.GetBytes(password), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256);
        var aesKey = deriveBytes.GetBytes(KeySize);

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
            throw new InvalidDataException("הקובץ אינו מכיל מפתחות.");

        var imported = 0;
        foreach (var (key, value) in secrets)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                // Normalize connection strings: fix double backslash from JSON copy-paste
                var normalizedValue = key.StartsWith(SecretKeys.ConnectionStringPrefix, StringComparison.Ordinal)
                    ? NormalizeConnectionString(value)
                    : value;
                CredentialVaultService.SetSecret(key, normalizedValue);
                imported++;
            }
        }

        return imported;
    }

    /// <summary>
    /// Fixes double backslash in Data Source and ensures TrustServerCertificate=True.
    /// Users often copy connection strings from JSON or C# code where backslash is escaped.
    /// </summary>
    private static string NormalizeConnectionString(string raw)
    {
        try
        {
            var csb = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(raw);
            if (csb.DataSource.Contains("\\\\"))
                csb.DataSource = csb.DataSource.Replace("\\\\", "\\");
            if (!csb.TrustServerCertificate)
                csb.TrustServerCertificate = true;
            return csb.ConnectionString;
        }
        catch
        {
            return raw;
        }
    }

    /// <summary>
    /// Checks if the given file is a valid SiNet provisioning file (by magic header).
    /// </summary>
    public static bool IsProvisioningFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < 8) return false; // magic + version minimum

            Span<byte> header = stackalloc byte[4];
            fs.ReadExactly(header);
            return header.SequenceEqual(_magic);
        }
        catch
        {
            return false;
        }
    }
}
