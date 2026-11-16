using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed partial class PortsCommandTests
{
    [Fact]
    public async Task BootstrapOnlyRefreshRejectsAnOffsetWithoutAnyRuntimeOperation()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var exit = await fixture.Run("--refresh-https-bootstrap", "--offset", "10000");

        // Assert
        exit.Should().Be(1);
        fixture
            .Console.AllText.Should()
            .Contain("cannot be combined")
            .And.Contain("changes no ports");
        fixture.Sbx.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ExplicitBootstrapRefreshUsesEmbeddedReceiverWithoutDiscoveryExportOrRootTrust()
    {
        // Arrange
        var runner = Substitute.For<IProcessRunner>();
        using var fixture = new Fixture(runner: runner);
        fixture.Root.File("folder/abox.json", "invalid unrelated folder configuration");
        fixture.Root.File("folder/Web.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\">");
        var calls = new List<ProcessRequest>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessRequest>();
                calls.Add(request);
                return new ProcessResult(
                    0,
                    request.Arguments[0] switch
                    {
                        "version" => """{"client":{"version":"0.51.0"}}""",
                        "diagnose" => """{"checks":[]}""",
                        "ls" => $$"""[{"name":"{{fixture.Name}}","status":"running"}]""",
                        "exec" when request.Arguments.Contains("refresh-bootstrap") => "",
                        _ => throw new InvalidOperationException("Unexpected runtime operation."),
                    },
                    "private child diagnostics"
                );
            });

        // Act
        var exit = await fixture.Run("--refresh-https-bootstrap");

        // Assert
        exit.Should().Be(0);
        fixture.Console.AllText.Should().Contain("bootstrap refreshed").And.Contain("not verified");
        fixture
            .Console.AllText.Should()
            .NotContain("private child")
            .And.NotContain("ports published");
        var refresh = calls.Single(call => call.Arguments[0] == "exec");
        refresh.Arguments.Should().ContainInOrder(fixture.Name, "python3", "-I", "-c");
        refresh.Arguments.Should().NotContain("root");
        refresh.StandardInput.Should().BeEmpty();
        refresh.AttachConsole.Should().BeFalse();
        refresh.OutputPolicy.Should().Be(ProcessOutputPolicy.Sensitive);
        calls.Should().OnlyContain(call => call.FileName == "sbx");
    }

    [Fact]
    public async Task BootstrapOnlyUpgradeThroughComposedCliPreservesActivationAndNeverRunsOldCode()
    {
        // Arrange: explicit disposable Linux container only, as its application user.
        if (
            !OperatingSystem.IsLinux()
            || !File.Exists("/.dockerenv")
            || Environment.GetEnvironmentVariable("ABOX_DISPOSABLE_RECEIVER_CHECKS") != "1"
            || Environment.UserName == "root"
        )
            throw new InvalidOperationException(
                Xunit.v3.DynamicSkipToken.Value + "Requires the non-root disposable Linux runner."
            );
        var local = new ProcessRunner();
        var identity = await local.RunAsync(
            new ProcessRequest("id", ["-u"]),
            CancellationToken.None
        );
        var uid = identity.StandardOutput.Trim();
        uid.Should().MatchRegex("^[1-9][0-9]{0,9}$");
        var runtime = "/tmp/abox-https-" + uid;
        Directory
            .Exists(runtime)
            .Should()
            .BeFalse("the disposable must have no previous HTTPS state");
        var runner = Substitute.For<IProcessRunner>();
        using var fixture = new Fixture(runner: runner);
        var calls = new List<ProcessRequest>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var request = call.Arg<ProcessRequest>();
                calls.Add(request);
                if (request.Arguments[0] != "exec")
                    return new ProcessResult(
                        0,
                        request.Arguments[0] switch
                        {
                            "version" => """{"client":{"version":"0.51.0"}}""",
                            "diagnose" => """{"checks":[]}""",
                            "ls" => $$"""[{"name":"{{fixture.Name}}","status":"running"}]""",
                            _ => throw new InvalidOperationException(
                                "Unexpected runtime operation."
                            ),
                        },
                        ""
                    );
                // Replace only sbx transport; execute the exact production argv/source locally.
                // Public root trust is irrelevant to a reader-only upgrade and is not changed here.
                if (request.Arguments.Contains("root"))
                    return new ProcessResult(0, "", "");
                var arguments = request
                    .Arguments.SkipWhile(arg => arg != fixture.Name)
                    .Skip(1)
                    .ToArray();
                return await local.RunAsync(
                    request with
                    {
                        FileName = arguments[0],
                        Arguments = arguments[1..],
                        AttachConsole = false,
                        OutputPolicy = ProcessOutputPolicy.CapturedSensitive,
                    },
                    call.Arg<CancellationToken>()
                );
            });
        var client = new SbxClient(runner, AboxPaths.For(fixture.Root.Path, fixture.Root.Path));
        using var key = RSA.Create(2048);
        var certificateRequest = new CertificateRequest(
            "CN=localhost",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        using var certificate = certificateRequest.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1)
        );
        var password = new string('a', 64);
        try
        {
            await client.ActivateDevelopmentCertificateAsync(
                fixture.Name,
                certificate.Export(X509ContentType.Pfx, password),
                password,
                CancellationToken.None
            );
            var bootstrap = Path.Combine(runtime, "bootstrap.py");
            var verified = await File.ReadAllBytesAsync(bootstrap);
            var marker = Path.Combine(fixture.Location, "unverified-code-ran");
            await File.WriteAllTextAsync(
                bootstrap,
                $"open({System.Text.Json.JsonSerializer.Serialize(marker)}, 'w').write('unsafe')"
            );
            (
                await client.ExecAsync(
                    new SbxExecRequest(fixture.Name, ["true"]),
                    CancellationToken.None
                )
            )
                .Should()
                .Be(78);
            var retained = Directory
                .GetFiles(runtime)
                .Where(path => path != bootstrap)
                .ToDictionary(path => path, File.ReadAllBytes);
            fixture.Root.File("folder/abox.json", "invalid unrelated folder configuration");
            fixture.Root.File("folder/Web.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\">");
            calls.Clear();

            // Act
            var exit = await fixture.Run("--refresh-https-bootstrap");

            // Assert: real CLI -> SbxClient -> embedded receiver; no activation/export repair.
            exit.Should().Be(0, fixture.Console.AllText);
            File.Exists(marker).Should().BeFalse();
            (await File.ReadAllBytesAsync(bootstrap)).Should().Equal(verified);
            foreach (var pair in retained)
                (await File.ReadAllBytesAsync(pair.Key)).Should().Equal(pair.Value);
            calls.Count(call => call.Arguments[0] == "exec").Should().Be(1);
            calls
                .Should()
                .NotContain(call =>
                    call.Arguments.Contains("root")
                    || call.Arguments.Contains("activate")
                    || call.Arguments.Contains("--publish")
                );
            (
                await client.ExecAsync(
                    new SbxExecRequest(
                        fixture.Name,
                        [
                            "sh",
                            "-c",
                            "test \"$ASPNETCORE_Kestrel__Certificates__Default__Path\" = \"$1\" && test \"$ASPNETCORE_Kestrel__Certificates__Default__Password\" = \"$2\"",
                            "_",
                            runtime + "/active.pfx",
                            password,
                        ]
                    ),
                    CancellationToken.None
                )
            )
                .Should()
                .Be(0);
        }
        finally
        {
            if (Directory.Exists(runtime))
                Directory.Delete(runtime, true);
        }
    }
}
