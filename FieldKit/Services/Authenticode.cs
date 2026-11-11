using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace FieldKit.Services;

/// <summary>
/// Checks that a downloaded executable carries a valid Authenticode signature
/// from Microsoft. FieldKit runs elevated and leaves these files on someone's PC,
/// so nothing that fails this check is kept.
/// </summary>
public static class Authenticode
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_WHOLECHAIN = 1;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_IGNORE = 0;

    public static bool IsSignedByMicrosoft(string path, out string reason)
    {
        var trust = VerifyTrust(path);
        if (trust != 0)
        {
            reason = $"signature check failed (0x{trust:X8})";
            return false;
        }

        try
        {
            // WinVerifyTrust above has already validated the chain to a trusted
            // root; this only reads who the signer is.
#pragma warning disable SYSLIB0057 // No non-obsolete API reads the Authenticode signer of a PE file.
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            var organization = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            if (!organization.Equals("Microsoft Corporation", StringComparison.Ordinal)
                && !organization.Equals("Microsoft Windows", StringComparison.Ordinal))
            {
                reason = $"signed by \"{organization}\", not Microsoft";
                return false;
            }

            // The name alone is not proof: any root this PC trusts could issue a
            // certificate called "Microsoft Corporation". Require the chain to end
            // at one of Microsoft's own roots.
            if (!ChainsToMicrosoftRoot(certificate))
            {
                reason = "signed in Microsoft's name, but the certificate does not chain to a Microsoft root";
                return false;
            }

            reason = "";
            return true;
        }
        catch (CryptographicException ex)
        {
            reason = $"signer could not be read ({ex.Message})";
            return false;
        }
    }

    private static readonly IntPtr CERT_CHAIN_POLICY_MICROSOFT_ROOT = 7;

    // Also accept Microsoft's application-signing roots (for example "Microsoft
    // Root Certificate Authority 2011"), which sign Sysinternals and most tools.
    // Without this flag the policy accepts only the Windows product roots.
    private const uint MICROSOFT_ROOT_CERT_CHAIN_POLICY_CHECK_APPLICATION_ROOT_FLAG = 0x00020000;

    internal static bool ChainsToMicrosoftRoot(X509Certificate2 certificate)
    {
        using var chain = new X509Chain();
        // WinVerifyTrust has already judged validity, timestamp and revocation.
        // This chain exists only to find the root, and signing certificates are
        // routinely past their own expiry while the signature stays valid.
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.IgnoreNotTimeValid;
        chain.Build(certificate);
        if (chain.ChainElements.Count == 0) return false;

        var parameters = new CERT_CHAIN_POLICY_PARA
        {
            cbSize = (uint)Marshal.SizeOf<CERT_CHAIN_POLICY_PARA>(),
            dwFlags = MICROSOFT_ROOT_CERT_CHAIN_POLICY_CHECK_APPLICATION_ROOT_FLAG
        };
        var status = new CERT_CHAIN_POLICY_STATUS { cbSize = (uint)Marshal.SizeOf<CERT_CHAIN_POLICY_STATUS>() };

        return CertVerifyCertificateChainPolicy(CERT_CHAIN_POLICY_MICROSOFT_ROOT, chain.ChainContext, ref parameters, ref status)
               && status.dwError == 0;
    }

    [DllImport("crypt32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CertVerifyCertificateChainPolicy(
        IntPtr pszPolicyOID, IntPtr pChainContext, ref CERT_CHAIN_POLICY_PARA pPolicyPara, ref CERT_CHAIN_POLICY_STATUS pPolicyStatus);

    [StructLayout(LayoutKind.Sequential)]
    private struct CERT_CHAIN_POLICY_PARA
    {
        public uint cbSize;
        public uint dwFlags;
        public IntPtr pvExtraPolicyPara;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CERT_CHAIN_POLICY_STATUS
    {
        public uint cbSize;
        public uint dwError;
        public int lChainIndex;
        public int lElementIndex;
        public IntPtr pvExtraPolicyStatus;
    }

    private static int VerifyTrust(string path)
    {
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = path
        };

        var fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        var dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_WHOLECHAIN,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = fileInfoPtr,
                dwStateAction = WTD_STATEACTION_IGNORE
            };
            Marshal.StructureToPtr(data, dataPtr, false);

            return WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, dataPtr);
        }
        finally
        {
            Marshal.DestroyStructure<WINTRUST_FILE_INFO>(fileInfoPtr);
            Marshal.FreeHGlobal(fileInfoPtr);
            Marshal.FreeHGlobal(dataPtr);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, IntPtr pWVTData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }
}
