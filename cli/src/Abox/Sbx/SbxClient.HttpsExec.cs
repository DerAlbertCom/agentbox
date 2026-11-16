namespace Abox.Sbx;

public sealed partial class SbxClient
{
    private static readonly Lazy<string> DevelopmentCertificateExecBootstrap = new(() =>
        ReadDevelopmentCertificateResource("Abox.DevelopmentCertificateExec")
    );
}
