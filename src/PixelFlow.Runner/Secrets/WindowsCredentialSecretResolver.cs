using System.Runtime.InteropServices;
using System.Text;

namespace PixelFlow.Runner.Secrets;

/// <summary>
/// Windows Credential Manager (Generic) resolver for Type step <c>secretRef</c> values (P30).
/// Also exposes store/delete helpers for tests and local setup — never used by the Runner engine itself.
/// </summary>
public sealed class WindowsCredentialSecretResolver : PixelFlow.Core.Secrets.ISecretResolver
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;

    public bool TryResolve(string secretRef, out string secret, out string? error)
    {
        secret = "";
        error = null;

        if (string.IsNullOrWhiteSpace(secretRef))
        {
            error = "Secret reference is empty.";
            return false;
        }

        if (!CredReadW(secretRef.Trim(), CredTypeGeneric, 0, out var credPtr))
        {
            var code = Marshal.GetLastWin32Error();
            error = code == 1168
                ? $"Secret '{secretRef}' was not found in Windows Credential Manager."
                : $"Failed to read secret '{secretRef}' from Credential Manager (Win32 {code}).";
            return false;
        }

        try
        {
            var cred = Marshal.PtrToStructure<NativeCredential>(credPtr);
            if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0)
            {
                error = $"Secret '{secretRef}' has an empty credential blob.";
                return false;
            }

            // Generic credentials store Unicode bytes (UTF-16LE) for the password/secret.
            var byteCount = (int)cred.CredentialBlobSize;
            var bytes = new byte[byteCount];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, byteCount);
            secret = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
            return true;
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    /// <summary>Creates or updates a Generic credential (test / local setup helper).</summary>
    public static void Store(string secretRef, string secretValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretRef);
        ArgumentNullException.ThrowIfNull(secretValue);

        var blob = Encoding.Unicode.GetBytes(secretValue);
        var target = Marshal.StringToCoTaskMemUni(secretRef.Trim());
        var user = Marshal.StringToCoTaskMemUni(Environment.UserName);
        var blobPtr = Marshal.AllocCoTaskMem(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var cred = new NativeCredential
            {
                Flags = 0,
                Type = CredTypeGeneric,
                TargetName = target,
                Comment = IntPtr.Zero,
                LastWritten = default,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPtr,
                Persist = CredPersistLocalMachine,
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                TargetAlias = IntPtr.Zero,
                UserName = user,
            };

            if (!CredWriteW(ref cred, 0))
            {
                throw new InvalidOperationException(
                    $"CredWrite failed for '{secretRef}' (Win32 {Marshal.GetLastWin32Error()}).");
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(target);
            Marshal.FreeCoTaskMem(user);
            Marshal.FreeCoTaskMem(blobPtr);
        }
    }

    /// <summary>Deletes a Generic credential if present (test cleanup).</summary>
    public static void Delete(string secretRef)
    {
        if (string.IsNullOrWhiteSpace(secretRef))
        {
            return;
        }

        CredDeleteW(secretRef.Trim(), CredTypeGeneric, 0);
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredReadW(string targetName, uint type, uint flags, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWriteW(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDeleteW(string targetName, uint type, uint flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }
}
