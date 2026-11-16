using Abox.ApplicationPorts;
using Abox.Https;
using Abox.Infrastructure;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class DevelopmentHttpsTests
{
    [Theory]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk.Web\" />", true)]
    [InlineData("<Project><Sdk Name=\"Microsoft.NET.Sdk.Web\" /></Project>", true)]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk\" />", false)]
    [InlineData(
        "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><FrameworkReference Include=\"Microsoft.AspNetCore.App\" /></ItemGroup></Project>",
        true
    )]
    public async Task OnlyExplicitApplicableProjectsTriggerCertificateSetupEvenWithoutLaunchPorts(
        string project,
        bool applicable
    )
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        await File.WriteAllTextAsync(Path.Combine(root.Path, "App.csproj"), project);
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "[]", ""));
        var sbx = Substitute.For<ISbxClient>();
        sbx.GetDevelopmentCertificatePasswordAsync("actual", Arg.Any<CancellationToken>())
            .Returns(new string('a', 64));
        var console = new TestConsole();
        var exporter = new HostDevelopmentCertificate(runner, console, root.Path, [new("tool", 6)]);
        var ports = new AppPorts(sbx, console, new DevelopmentHttps(exporter, sbx, console));

        // Act
        var action = () => ports.PublishAsync(root.Path, "actual", 10000, CancellationToken.None);

        // Assert
        if (applicable)
        {
            await action.Should().ThrowAsync<AboxException>();
            await sbx.Received(1)
                .GetDevelopmentCertificatePasswordAsync("actual", Arg.Any<CancellationToken>());
        }
        else
        {
            await action();
            await sbx.DidNotReceive()
                .GetDevelopmentCertificatePasswordAsync(
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>()
                );
            await runner
                .DidNotReceive()
                .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
        }
    }

    [Theory]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk\"><broken>")]
    [InlineData(
        "<!DOCTYPE Project [<!ENTITY x SYSTEM 'file:///never-read'>]><Project Sdk=\"Microsoft.NET.Sdk\" />"
    )]
    public async Task UnsupportedUnrelatedProjectXmlDoesNotBlockValidPortDiscovery(string unrelated)
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Unrelated.csproj"), unrelated);
        Directory.CreateDirectory(Path.Combine(root.Path, "Properties"));
        await File.WriteAllTextAsync(
            Path.Combine(root.Path, "Properties", "launchSettings.json"),
            "{\"profiles\":{\"app\":{\"applicationUrl\":\"http://localhost:8203\"}}}"
        );

        // Act
        var plan = await AppPortDiscovery.DiscoverAsync(root.Path, CancellationToken.None);

        // Assert
        plan.Occurrences.Select(occurrence => occurrence.Port).Should().Equal(8203);
        plan.HasDotnetProjects.Should().BeFalse();
    }

    [Theory]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk.Web\"><broken>")]
    [InlineData(
        "<!DOCTYPE Project [<!ENTITY x SYSTEM 'file:///never-read'>]><Project Sdk=\"Microsoft.NET.Sdk.Web\" />"
    )]
    [InlineData(
        "<Project><ItemGroup><FrameworkReference Include=\"Microsoft.AspNetCore.App\" /><broken>"
    )]
    public async Task MalformedExplicitAspNetProjectFailsClosed(string project)
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Web.csproj"), project);

        // Act
        var action = () => AppPortDiscovery.DiscoverAsync(root.Path, CancellationToken.None);

        // Assert
        await action.Should().ThrowAsync<AboxException>();
    }
}
