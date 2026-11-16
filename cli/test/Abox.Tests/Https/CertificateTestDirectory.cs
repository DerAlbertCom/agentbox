namespace Abox.Tests;

/// <summary>Synthetic certificate fixtures stay beside the test executable, not in host-export staging.</summary>
internal sealed class CertificateTestDirectory : IDisposable
{
    public CertificateTestDirectory()
    {
        Path = System.IO.Path.Combine(
            AppContext.BaseDirectory,
            "certificate-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
