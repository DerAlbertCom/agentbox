using System.Security.Cryptography;
using Abox.Infrastructure;

namespace Abox.Https;

/// <summary>CLI-only export of an already eligible, exactly correlated trusted identity.</summary>
public sealed class HostDevelopmentCertificate(IProcessRunner runner, IConsole console)
{
    private readonly string temporaryParent = Path.GetTempPath();
    private readonly IReadOnlyList<DevelopmentCertificateTool>? tools;

    internal HostDevelopmentCertificate(
        IProcessRunner runner,
        IConsole console,
        string temporaryParent,
        IReadOnlyList<DevelopmentCertificateTool>? tools
    )
        : this(runner, console)
    {
        this.temporaryParent = temporaryParent;
        this.tools = tools;
    }

    public async Task<ExportedDevelopmentCertificate> ExportAsync(
        string password,
        CancellationToken token
    )
    {
        token.ThrowIfCancellationRequested();
        try
        {
            var installed =
                tools ?? await new InstalledDevelopmentCertificateTools(runner).FindAsync(token);
            foreach (var tool in installed)
            {
                var transaction = new DevelopmentCertificateExport(
                    runner,
                    console,
                    temporaryParent,
                    tool
                );
                // The CLI has no thumbprint selector. Ambiguity is not permission to
                // let it choose an untrusted candidate or generate a replacement.
                var identity = await transaction.ReadEligibleIdentityAsync(token);
                if (identity is not null)
                    return await transaction.ExportAsync(identity, password, token);
            }
            throw Failure();
        }
        catch (OperationCanceledException)
        {
            throw new OperationCanceledException(
                "Development certificate export canceled; private staging cleanup was attempted.",
                token
            );
        }
        catch (PrivateStagingException)
        {
            // Only our fixed staging diagnostic is safe to retain. Arbitrary
            // runner exceptions must still be sanitized by the catch below.
            throw;
        }
        catch (Exception)
        {
            throw Failure();
        }
    }

    private static AboxException Failure() =>
        new(
            "Cannot export an exact existing trusted ASP.NET Core development certificate. Require one date-valid, exportable CN=localhost identity with localhost DNS SAN and Full trust, eligible for an already-installed SDK dev-certs tool's current certificate format. Inspect 'dotnet dev-certs https --check --trust' and installed SDKs; resolve missing/untrusted/ambiguous/older-format certificates explicitly, then retry. abox will not install tools, generate an alternative, export through a native-store fallback, or change host trust. Do not renew/delete certificates concurrently with export. Child diagnostics were suppressed."
        );
}

public sealed class ExportedDevelopmentCertificate : IDisposable
{
    private readonly byte[] pfx;

    internal ExportedDevelopmentCertificate(byte[] pfx, string password)
    {
        this.pfx = pfx;
        Password = password;
    }

    public ReadOnlyMemory<byte> Pfx => pfx;
    public string Password { get; }

    public override string ToString() => "ExportedDevelopmentCertificate (private material hidden)";

    public void Dispose() => CryptographicOperations.ZeroMemory(pfx);
}
