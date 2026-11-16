using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

/// <summary>Simulates a CLI export failure without store access or external processes.</summary>
internal sealed class SyntheticCertificateExport : IDisposable
{
    private readonly string condition;
    private readonly SyntheticDevelopmentCertificate identity;
    private readonly CancellationTokenSource cancellation = new();
    private readonly byte[] bytes;
    private readonly JsonArray before;
    private readonly JsonArray after;
    private int reads;

    internal SyntheticCertificateExport(string condition, string password)
    {
        this.condition = condition;
        identity = new SyntheticDevelopmentCertificate(
            format: condition == "bad-format" ? 4 : 6,
            dnsName: condition == "bad-san" ? "not-localhost" : "localhost",
            before: condition == "future-pfx" ? DateTimeOffset.UtcNow.AddDays(1) : null,
            after: condition == "expired-pfx" ? DateTimeOffset.UtcNow.AddDays(-1) : null
        );
        bytes =
            condition == "no-key" ? identity.ExportPublicOnly(password) : identity.Export(password);
        before = EligibleReport();
        after = FreshReport();
        Runner = Substitute.For<IProcessRunner>();
        Runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Respond(call.Arg<ProcessRequest>()));
    }

    internal IProcessRunner Runner { get; }
    internal CancellationToken Token => cancellation.Token;

    private JsonArray EligibleReport()
    {
        var metadata = identity.Metadata();
        metadata[0]!["Version"] = 6;
        if (condition == "wrong-identity")
            metadata[0]!["Thumbprint"] = new string('A', 40);
        if (condition == "future-pfx")
            metadata[0]!["ValidityNotBefore"] = DateTime.UtcNow.AddDays(-1);
        if (condition == "expired-pfx")
            metadata[0]!["ValidityNotAfter"] = DateTime.UtcNow.AddDays(1);
        return metadata;
    }

    private JsonArray FreshReport()
    {
        var metadata = before.DeepClone().AsArray();
        switch (condition)
        {
            case "post-untrusted":
                metadata[0]!["TrustLevel"] = "None";
                break;
            case "post-empty":
                metadata.Clear();
                break;
            case "post-multiple":
                metadata.Add(metadata[0]!.DeepClone());
                break;
            case "post-renewed":
                metadata[0]!["Thumbprint"] = new string('B', 40);
                break;
            case "post-version":
                metadata[0]!["Version"] = 7;
                break;
            case "post-valid-from":
                metadata[0]!["ValidityNotBefore"] = identity
                    .Certificate.NotBefore.ToUniversalTime()
                    .AddDays(-1);
                break;
            case "post-valid-until":
                metadata[0]!["ValidityNotAfter"] = identity
                    .Certificate.NotAfter.ToUniversalTime()
                    .AddDays(1);
                break;
        }
        return metadata;
    }

    private Task<ProcessResult> Respond(ProcessRequest child)
    {
        if (!child.Arguments.Contains("--export-path"))
        {
            if (condition == "post-cancel" && reads != 0)
                return Cancel();
            return Task.FromResult(
                new ProcessResult(
                    0,
                    (reads++ == 0 ? before : after).ToJsonString(),
                    "secret metadata"
                )
            );
        }
        var path = child.Arguments[Array.IndexOf(child.Arguments.ToArray(), "--export-path") + 1];
        WriteExport(path);
        if (condition == "cancel")
            return Cancel();
        if (condition == "throw")
            return Task.FromException<ProcessResult>(new IOException("secret export"));
        return Task.FromResult(
            new ProcessResult(
                condition == "partial-export" ? 9 : 0,
                "secret export",
                "secret password"
            )
        );
    }

    private void WriteExport(string path)
    {
        if (condition == "missing-pfx")
            return;
        File.WriteAllBytes(
            path,
            condition switch
            {
                "empty-pfx" => [],
                "oversized-pfx" => new byte[1024 * 1024 + 1],
                "invalid-pfx" => [1, 2, 3],
                _ => bytes,
            }
        );
    }

    private Task<ProcessResult> Cancel()
    {
        cancellation.Cancel();
        return Task.FromException<ProcessResult>(
            new OperationCanceledException("secret cancellation", cancellation.Token)
        );
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(bytes);
        identity.Dispose();
        cancellation.Dispose();
    }
}
