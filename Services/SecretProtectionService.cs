using System.Runtime.InteropServices;
using System.Text;

namespace UnifiedGameLauncher.Services;

/// <summary>
/// Protege segredos usando a Data Protection API (DPAPI) do Windows no escopo do usuário atual.
/// Os valores protegidos só podem ser descriptografados pela mesma conta do Windows.
/// </summary>
public static class SecretProtectionService
{
    private const uint CryptProtectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn,
        string? szDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        out DataBlob pDataOut);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn,
        IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        out DataBlob pDataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static string? Protect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToBase64String(ProtectBytes(bytes));
    }

    public static string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue)) return null;
        try
        {
            var bytes = Convert.FromBase64String(protectedValue);
            return Encoding.UTF8.GetString(UnprotectBytes(bytes));
        }
        catch
        {
            return null;
        }
    }

    private static byte[] ProtectBytes(byte[] bytes)
    {
        var input = ToBlob(bytes);
        try
        {
            if (!CryptProtectData(ref input, "LUDARYX", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, out var output))
                throw new InvalidOperationException($"DPAPI falhou ao proteger um segredo (Win32 {Marshal.GetLastWin32Error()}).");
            try { return FromBlob(output); }
            finally { if (output.pbData != IntPtr.Zero) LocalFree(output.pbData); }
        }
        finally { FreeBlob(input); }
    }

    private static byte[] UnprotectBytes(byte[] bytes)
    {
        var input = ToBlob(bytes);
        try
        {
            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, out var output))
                throw new InvalidOperationException($"DPAPI falhou ao descriptografar um segredo (Win32 {Marshal.GetLastWin32Error()}).");
            try { return FromBlob(output); }
            finally { if (output.pbData != IntPtr.Zero) LocalFree(output.pbData); }
        }
        finally { FreeBlob(input); }
    }

    private static DataBlob ToBlob(byte[] bytes)
    {
        var ptr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        return new DataBlob { cbData = bytes.Length, pbData = ptr };
    }

    private static byte[] FromBlob(DataBlob blob)
    {
        var bytes = new byte[blob.cbData];
        if (blob.cbData > 0) Marshal.Copy(blob.pbData, bytes, 0, blob.cbData);
        return bytes;
    }

    private static void FreeBlob(DataBlob blob)
    {
        if (blob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(blob.pbData);
    }
}
