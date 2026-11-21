using System.Security.Cryptography.X509Certificates;
using System.Text;
using Abox.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class AppPortsLifecycleHttpsTests
{
    [Theory]
    [InlineData("App.csproj", "utf-8", "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />")]
    [InlineData(
        "App.fsproj",
        "utf-16",
        "<Project Sdk=\"Microsoft.NET.Sdk; Microsoft.NET.Sdk.Web/10.0.100\" />"
    )]
    [InlineData(
        "App.vbproj",
        "utf-16BE",
        "<Project><Sdk Name=\"Microsoft.NET.Sdk.Web/10.0.100\" /></Project>"
    )]
    [InlineData(
        "App.CSPROJ",
        "utf-32",
        "<Project xmlns=\"urn:synthetic\"><ItemGroup><FrameworkReference Include=\"Microsoft.AspNetCore.App\" /></ItemGroup></Project>"
    )]
    [InlineData("App.FSPROJ", "utf-32BE", "<Project Sdk=\"microsoft.net.sdk.web\" />")]
    public async Task StaticEncodedProjectDeclarationsConfigureHttpsWithoutInferringPorts(
        string file,
        string encoding,
        string project
    )
    {
        // Arrange
        using var fixture = new AppPortsLifecycleFixture();
        using var certificate = new LifecycleCertificateCli(fixture.Console);
        fixture.Services.AddSingleton(certificate.Exporter);
        var path = fixture.Root.File("folder/" + file, "");
        await File.WriteAllTextAsync(path, project, Encoding.GetEncoding(encoding));
        fixture
            .Sbx.GetDevelopmentCertificatePasswordAsync(fixture.Name, Arg.Any<CancellationToken>())
            .Returns(new string('a', 64));

        // Act
        var exit = await fixture.Create(false);

        // Assert
        exit.Should().Be(0, fixture.Console.AllText);
        certificate.Exports.Should().Be(1);
        fixture
            .Console.AllText.Should()
            .Contain("No explicit application ports")
            .And.Contain("Created sandbox");
        await fixture
            .Sbx.DidNotReceive()
            .EnsureApplicationPortsAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<ApplicationPortMapping>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MixedCreationCopiesSecretsThenPublishesAndActivatesBeforeSuccess(bool local)
    {
        // Arrange
        using var fixture = new AppPortsLifecycleFixture();
        using var certificate = new LifecycleCertificateCli(fixture.Console);
        fixture.Services.AddSingleton(certificate.Exporter);
        fixture.Web();
        fixture.Angular();
        fixture.Configure("""{"squad":"ac","userSecrets":["sample"],"portOffset":20000}""");
        fixture.Root.File("appdata/Microsoft/UserSecrets/sample/secrets.json", "{}");
        var events = new List<string>();
        fixture
            .Sbx.WriteUserSecretsAsync(fixture.Name, "sample", "{}", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                events.Add("secrets");
                return Task.CompletedTask;
            });
        fixture
            .Sbx.EnsureApplicationPortsAsync(
                fixture.Name,
                Arg.Any<IReadOnlyList<ApplicationPortMapping>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                events.Add("ports");
                return Task.CompletedTask;
            });
        fixture
            .Sbx.ActivateDevelopmentCertificateAsync(
                fixture.Name,
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                events.Add("activate");
                return Task.CompletedTask;
            });

        // Act
        var exit = await fixture.Create(local);

        // Assert
        exit.Should().Be(0, fixture.Console.AllText);
        events.Should().Equal("secrets", "ports", "activate");
        certificate.Exports.Should().Be(1);
        await fixture
            .Sbx.Received(1)
            .VerifyDevelopmentCertificateActivationAsync(
                fixture.Name,
                Arg.Any<CancellationToken>()
            );
        fixture
            .Console.AllText.Should()
            .Contain("| 4200 | 24200")
            .And.Contain("| 8203 | 28203")
            .And.Contain("Created sandbox")
            .And.NotContain("private synthetic");
    }

    [Theory]
    [InlineData("published")]
    [InlineData("local")]
    [InlineData("run")]
    public async Task ReplacementExportsTheCurrentIdentityAgainBeforeAnyLauncherStarts(string entry)
    {
        // Arrange
        using var fixture = new AppPortsLifecycleFixture();
        using var certificate = new LifecycleCertificateCli(fixture.Console);
        fixture.Services.AddSingleton(certificate.Exporter);
        fixture.Web();
        fixture.Angular();
        var installed = new List<string>();
        fixture
            .Sbx.ActivateDevelopmentCertificateAsync(
                fixture.Name,
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                using var pfx = X509CertificateLoader.LoadPkcs12(
                    call.Arg<ReadOnlyMemory<byte>>().Span,
                    call.ArgAt<string>(2),
                    X509KeyStorageFlags.EphemeralKeySet
                );
                installed.Add(pfx.Thumbprint);
                return Task.CompletedTask;
            });
        (await fixture.Create(false)).Should().Be(0, fixture.Console.AllText);
        var first = certificate.Current.Thumbprint;
        certificate.Renew();
        fixture.Existing();
        fixture
            .Sbx.ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                installed.Should().HaveCount(2);
                return 0;
            });

        // Act
        var exit = await fixture.Replace(entry);

        // Assert
        exit.Should().Be(0, fixture.Console.AllText);
        installed.Should().Equal(first, certificate.Current.Thumbprint);
        certificate.Exports.Should().Be(2);
    }
}
