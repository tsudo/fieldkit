using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FieldKit.Services;
using Xunit;

namespace FieldKit.Tests;

public class AuthenticodeTests
{
    [Fact]
    public void A_chain_ending_at_microsofts_application_root_is_accepted()
    {
        // The .NET runtime's own library is signed through "Microsoft Root
        // Certificate Authority 2011", the same root Sysinternals chains to.
        // Its signer is named ".NET", so only the root rule is tested here.
#pragma warning disable SYSLIB0057 // Same API the product uses to read a PE file's signer.
        using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(typeof(object).Assembly.Location));
#pragma warning restore SYSLIB0057

        Assert.True(Authenticode.ChainsToMicrosoftRoot(signer));
    }

    [Fact]
    public void A_certificate_that_only_claims_microsofts_name_is_rejected()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Microsoft Corporation, O=Microsoft Corporation", key,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var impostor = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1));

        Assert.Equal("Microsoft Corporation", impostor.GetNameInfo(X509NameType.SimpleName, forIssuer: false));
        Assert.False(Authenticode.ChainsToMicrosoftRoot(impostor));
    }

    [Fact]
    public void The_runtime_library_is_rejected_because_its_signer_is_not_named_microsoft()
    {
        // Validly signed, Microsoft root, but the signer is ".NET": the name rule still applies.
        Assert.False(Authenticode.IsSignedByMicrosoft(typeof(object).Assembly.Location, out var reason));
        Assert.Contains("not Microsoft", reason);
    }

    [Fact]
    public void An_unsigned_file_is_rejected()
    {
        Assert.False(Authenticode.IsSignedByMicrosoft(typeof(AuthenticodeTests).Assembly.Location, out var reason));
        Assert.Contains("signature check failed", reason);
    }

    [Fact]
    public void A_file_that_is_not_a_program_is_rejected()
    {
        var text = Path.Combine(Path.GetTempPath(), $"fieldkit-auth-{Guid.NewGuid():N}.txt");
        File.WriteAllText(text, "not a program");
        try
        {
            Assert.False(Authenticode.IsSignedByMicrosoft(text, out _));
        }
        finally
        {
            File.Delete(text);
        }
    }

    [Fact]
    public void Microsoft_edge_passes_the_whole_check_where_it_is_installed()
    {
        // Edge carries an embedded "Microsoft Corporation" signature. It ships with
        // Windows 10 and 11 and with GitHub's Windows runners. Where it is absent
        // this test proves nothing, and says so by returning early.
        var edge = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft", "Edge", "Application", "msedge.exe");
        if (!File.Exists(edge)) return;

        Assert.True(Authenticode.IsSignedByMicrosoft(edge, out var reason), reason);
    }
}
