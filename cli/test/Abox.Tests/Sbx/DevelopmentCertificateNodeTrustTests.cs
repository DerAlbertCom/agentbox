using System.Security.Cryptography;
using System.Text;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

[Collection("Disposable certificate runtime")]
public sealed class DevelopmentCertificateNodeTrustTests
{
    [Fact]
    public async Task RenewalRetainsExactCallerBytesAndStableConfigurationAndBootstrap()
    {
        // Arrange
        using var scenario = await Scenario.Create();
        await scenario.Activate(scenario.Certificate.Export(Scenario.Password));
        var callerBytes = Encoding.UTF8.GetBytes("# corporate Grüße\r\n" + scenario.PublicPem);
        await scenario.SelectCallerInput(callerBytes);
        (await scenario.Launch()).Should().Be(0);
        var configuration = scenario.Read("config.json");
        var bootstrap = scenario.Read("bootstrap.py");
        using var renewed = new SyntheticDevelopmentCertificate();
        var renewedPfx = renewed.Export(Scenario.Password);

        // Act: an inherited owned bundle must read its original source, not its old leaf.
        scenario.Receiver.CallerNodeTrust = scenario.OwnedPath(callerBytes, ".pem");
        await scenario.Activate(renewedPfx);
        var launch = await scenario.Launch();

        // Assert
        launch.Should().Be(0);
        File.ReadAllBytes(scenario.OwnedPath(callerBytes, ".source")).Should().Equal(callerBytes);
        File.ReadAllBytes(scenario.OwnedPath(callerBytes, ".pem"))
            .Should()
            .Equal(
                callerBytes
                    .Concat(new byte[] { 10 })
                    .Concat(Encoding.ASCII.GetBytes(renewed.Certificate.ExportCertificatePem()))
            );
        scenario.Read("config.json").Should().Equal(configuration);
        scenario.Read("bootstrap.py").Should().Equal(bootstrap);
        scenario.Read("active.pfx").Should().Equal(renewedPfx);
    }

    [Fact]
    public async Task InvalidCallerInputRefusesReadinessButOrdinaryLaunchRetainsCallerSetting()
    {
        // Arrange
        using var scenario = await Scenario.Create();
        await scenario.Activate(scenario.Certificate.Export(Scenario.Password));
        await scenario.SelectCallerInput(Encoding.UTF8.GetBytes("not a PEM certificate"));

        // Act
        var readiness = () =>
            scenario.Client.VerifyDevelopmentCertificateActivationAsync(
                Scenario.Name,
                CancellationToken.None
            );
        var launch = await scenario.Client.ExecAsync(
            new SbxExecRequest(
                Scenario.Name,
                [
                    "sh",
                    "-c",
                    "test \"$NODE_EXTRA_CA_CERTS\" = \"$1\"",
                    "_",
                    scenario.Receiver.CallerNodeTrust!,
                ]
            ),
            CancellationToken.None
        );

        // Assert
        (await readiness.Should().ThrowAsync<AboxException>())
            .Which.Message.Should()
            .Contain("NODE_EXTRA_CA_CERTS");
        launch.Should().Be(0);
        Directory.GetFiles(scenario.Receiver.RuntimePath, "node-*").Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptOwnedSourceNeverUsesCallerRecoveryOrOverwritesTheBundle(bool validPem)
    {
        // Arrange
        using var scenario = await Scenario.Create();
        await scenario.Activate(scenario.Certificate.Export(Scenario.Password));
        var callerBytes = Encoding.UTF8.GetBytes(scenario.PublicPem);
        await scenario.SelectCallerInput(callerBytes);
        (await scenario.Launch()).Should().Be(0);
        var bundle = scenario.OwnedPath(callerBytes, ".pem");
        var retained = File.ReadAllBytes(bundle);
        scenario.Receiver.CallerNodeTrust = bundle;
        await File.WriteAllTextAsync(
            scenario.OwnedPath(callerBytes, ".source"),
            validPem ? "# changed source\n" + scenario.PublicPem : "corrupt synthetic owned source"
        );

        // Act
        var launch = await scenario.Launch();

        // Assert
        launch.Should().Be(78);
        File.ReadAllBytes(bundle).Should().Equal(retained);
    }

    [Fact]
    public async Task SeventeenthCallerVariantDoesNotEvictOrReplaceAnyOwnedTrustPair()
    {
        // Arrange
        using var scenario = await Scenario.Create();
        await scenario.Activate(scenario.Certificate.Export(Scenario.Password));
        for (var index = 0; index < 16; index++)
        {
            await scenario.SelectCallerInput(
                Encoding.UTF8.GetBytes($"# input {index}\n" + scenario.PublicPem)
            );
            (await scenario.Launch()).Should().Be(0);
        }
        var pairs = Directory
            .GetFiles(scenario.Receiver.RuntimePath, "node-*")
            .ToDictionary(path => path, File.ReadAllBytes);
        await scenario.SelectCallerInput(
            Encoding.UTF8.GetBytes("# seventeenth\n" + scenario.PublicPem)
        );

        // Act
        var readiness = () =>
            scenario.Client.VerifyDevelopmentCertificateActivationAsync(
                Scenario.Name,
                CancellationToken.None
            );
        var ordinaryLaunch = await scenario.Launch();

        // Assert
        (await readiness.Should().ThrowAsync<AboxException>())
            .Which.Message.Should()
            .Contain("NODE_EXTRA_CA_CERTS");
        ordinaryLaunch.Should().Be(0);
        Directory
            .GetFiles(scenario.Receiver.RuntimePath, "node-*")
            .Should()
            .BeEquivalentTo(pairs.Keys);
        foreach (var pair in pairs)
            File.ReadAllBytes(pair.Key).Should().Equal(pair.Value);
    }

    private sealed class Scenario : IDisposable
    {
        internal const string Name = "synthetic-private-runtime";
        internal static readonly string Password = new('a', 64);
        private readonly CertificateTestDirectory root = new();
        internal SyntheticDevelopmentCertificate Certificate { get; } = new();
        internal DisposableCertificateRuntime Receiver { get; }
        internal SbxClient Client { get; }
        internal string PublicPem => Certificate.Certificate.ExportCertificatePem();

        private Scenario(DisposableCertificateRuntime receiver)
        {
            Receiver = receiver;
            var runner = Substitute.For<IProcessRunner>();
            runner
                .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                    Receiver.RunAsync(call.Arg<ProcessRequest>(), call.Arg<CancellationToken>())
                );
            Client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));
        }

        internal static async Task<Scenario> Create() =>
            new(await DisposableCertificateRuntime.Create(Name));

        internal Task Activate(byte[] pfx) =>
            Client.ActivateDevelopmentCertificateAsync(Name, pfx, Password, CancellationToken.None);

        internal Task<int> Launch() =>
            Client.ExecAsync(new SbxExecRequest(Name, ["true"]), CancellationToken.None);

        internal byte[] Read(string name) =>
            File.ReadAllBytes(Path.Combine(Receiver.RuntimePath, name));

        internal string OwnedPath(byte[] bytes, string extension) =>
            Path.Combine(
                Receiver.RuntimePath,
                "node-" + Convert.ToHexStringLower(SHA256.HashData(bytes)) + extension
            );

        internal async Task SelectCallerInput(byte[] bytes)
        {
            Receiver.CallerNodeTrust = Path.Combine(root.Path, "caller.pem");
            await File.WriteAllBytesAsync(Receiver.CallerNodeTrust, bytes);
        }

        public void Dispose()
        {
            Receiver.Dispose();
            Certificate.Dispose();
            root.Dispose();
        }
    }
}
