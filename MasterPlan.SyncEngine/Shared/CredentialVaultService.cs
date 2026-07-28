using System.Runtime.InteropServices;
using System.Text;

namespace SiNetSQL.Services;

/// <summary>
/// Provides secure storage for application secrets using Windows Credential Manager.
/// Secrets are encrypted per-user via DPAPI — only the Windows user who stored them can retrieve them.
/// Visible in: Control Panel → Credential Manager → Windows Credentials (Generic).
/// </summary>
public static class CredentialVaultService
{
    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    /// <summary>
    /// Stores a secret in Windows Credential Manager.
    /// Overwrites any existing value for the same key.
    /// </summary>
    /// <param name="key">The target name (e.g., "SiNet/GeminiApiKey").</param>
    /// <param name="value">The secret value to store.</param>
    public static void SetSecret(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var bytes = Encoding.UTF8.GetBytes(value);
        var blobPtr = Marshal.AllocHGlobal(bytes.Length);

        try
        {
            Marshal.Copy(bytes, 0, blobPtr, bytes.Length);

            var credential = new CREDENTIAL
            {
                Flags = 0,
                Type = CRED_TYPE_GENERIC,
                TargetName = key,
                Comment = "SiNet Application Secret",
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blobPtr,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                TargetAlias = null,
                UserName = Environment.UserName
            };

            if (!CredWriteW(ref credential, 0))
            {
                var error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    $"CredWrite failed for '{key}'. Win32 error code: {error}");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }
    }

    /// <summary>
    /// Retrieves a secret from Windows Credential Manager.
    /// Returns null if the secret doesn't exist.
    /// </summary>
    public static string? GetSecret(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        if (!CredReadW(key, CRED_TYPE_GENERIC, 0, out var credPtr))
            return null;

        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
                return null;

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    /// <summary>
    /// Deletes a secret from Windows Credential Manager.
    /// Returns true if deleted, false if not found.
    /// </summary>
    public static bool DeleteSecret(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        return CredDeleteW(key, CRED_TYPE_GENERIC, 0);
    }

    /// <summary>
    /// Checks if a secret exists in Windows Credential Manager.
    /// </summary>
    public static bool HasSecret(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        if (!CredReadW(key, CRED_TYPE_GENERIC, 0, out var credPtr))
            return false;

        CredFree(credPtr);
        return true;
    }

    /// <summary>
    /// Returns a summary of which secrets are configured in the vault.
    /// </summary>
    public static Dictionary<string, bool> GetVaultStatus()
    {
        var status = new Dictionary<string, bool>();
        foreach (var key in SecretKeys.All)
        {
            status[key] = HasSecret(key);
        }
        return status;
    }

    /// <summary>
    /// Returns true if every secret required for WPF client launch is present.
    /// Host-only secrets listed in <see cref="SecretKeys.OptionalAtClientStartup"/>
    /// (e.g. AccService certificate password) do not block startup.
    /// </summary>
    public static bool IsVaultConfigured()
    {
        foreach (var key in SecretKeys.All)
        {
            if (SecretKeys.OptionalAtClientStartup.Contains(key))
                continue;

            if (!HasSecret(key))
                return false;
        }
        return true;
    }

    #region P/Invoke — Windows Credential Manager (advapi32.dll)

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWriteW(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredReadW(
        string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredDeleteW(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten; // FILETIME as 64-bit value
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    #endregion
}
