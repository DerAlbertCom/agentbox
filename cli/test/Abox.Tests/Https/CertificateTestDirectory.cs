namespace Abox.Tests;

/// <summary>Certificate fixtures stay in this checkout, never the blocked host-export staging directory.</summary>
internal sealed class CertificateTestDirectory : IDisposable
{
    public CertificateTestDirectory()
    {
        var checkout = new DirectoryInfo(AppContext.BaseDirectory);
        while (
            checkout is not null
            && !System.IO.File.Exists(
                System.IO.Path.Combine(checkout.FullName, "CODING_STANDARDS.md")
            )
        )
            checkout = checkout.Parent;
        if (checkout is null)
            throw new InvalidOperationException("Certificate fixtures require a source checkout.");
        Path = System.IO.Path.Combine(
            checkout.FullName,
            ".abox",
            "certificate-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
