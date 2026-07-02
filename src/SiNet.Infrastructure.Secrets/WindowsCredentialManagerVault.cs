using System.Runtime.InteropServices;
using System.Text;
using SiNet.Application.Configuration;

namespace SiNet.Infrastructure.Secrets;

/// <summary>
/// Windows Credential Manager vault for the native New System (separate type from SiNetSQL to avoid assembly conflicts in V2 host).
/// Reads/writes the same <c>SiNet/*</c> target names as legacy <c>CredentialVaultService</c>.
/// </summary>
internal static class WindowsCredentialManagerVault
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;

    public static void SetSecret(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var bytes = Encoding.UTF8.GetBytes(value);
        var blobPtr = Marshal.AllocHGlobal(bytes.Length);

        try
        {
            Marshal.Copy(bytes, 0, blobPtr, bytes.Length);

            var credential = new Credential
            {
                Flags = 0,
                Type = CredTypeGeneric,
                TargetName = key,
                Comment = "SiNet Application Secret",
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blobPtr,
                Persist = CredPersistLocalMachine,
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                TargetAlias = null,
                UserName = Environment.UserName,
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

    public static string? GetSecret(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (!CredReadW(key, CredTypeGeneric, 0, out var credPtr))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(credPtr);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
            {
                return null;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    public static bool HasSecret(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (!CredReadW(key, CredTypeGeneric, 0, out var credPtr))
        {
            return false;
        }

        CredFree(credPtr);
        return true;
    }

    public static IReadOnlyDictionary<string, bool> GetVaultStatus()
        => SecretCatalog.AllKeys.ToDictionary(k => k, HasSecret);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWriteW(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }
}
