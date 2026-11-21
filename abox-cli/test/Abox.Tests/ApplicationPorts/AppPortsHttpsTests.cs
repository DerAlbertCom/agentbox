using Abox.ApplicationPorts;
using Abox.Https;
using Abox.Infrastructure;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class AppPortsHttpsTests
{
    [Theory]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk.Web\" />", true)]
    [InlineData("<Project><Sdk Name=\"Microsoft.NET.Sdk.Web\" /></Project>", true)]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk\" />", false)]
    [InlineData(
        "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><FrameworkReference Include=\"Microsoft.AspNetCore.App\" /></ItemGroup></Project>",
        true
    )]
    public async Task OnlyExplicitApplicableProjectsWithLaunchPortsTriggerCertificateSetup(
        string project,
        bool applicable
    )
    {
        await AssertCertificateSetupAsync(project, launchPorts: true, expected: applicable);
    }

    [Theory]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk.Web\" />")]
    [InlineData(
        "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><FrameworkReference Include=\"Microsoft.AspNetCore.App\" /></ItemGroup></Project>"
    )]
    public async Task ProjectsWithoutLaunchPortsTriggerNoCertificateSetup(string project)
    {
        await AssertCertificateSetupAsync(project, launchPorts: false, expected: false);
    }

    private static async Task AssertCertificateSetupAsync(
        string project,
        bool launchPorts,
        bool expected
    )
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        await File.WriteAllTextAsync(Path.Combine(root.Path, "App.csproj"), project);
        if (launchPorts)
        {
            Directory.CreateDirectory(Path.Combine(root.Path, "Properties"));
            await File.WriteAllTextAsync(
                Path.Combine(root.Path, "Properties", "launchSettings.json"),
                """{"profiles":{"app":{"applicationUrl":"https://localhost:8203"}}}"""
            );
        }
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "[]", ""));
        var sbx = Substitute.For<ISbxClient>();
        sbx.GetDevelopmentCertificatePasswordAsync("actual", Arg.Any<CancellationToken>())
            .Returns(new string('a', 64));
        var console = new TestConsole();
        var exporter = new HostDevelopmentCertificate(runner, console, root.Path, [new("tool", 6)]);
        var ports = new AppPorts(sbx, console, new SandboxHttps(exporter, sbx, console));

        // Act
        var action = () => ports.PublishAsync(root.Path, "actual", 10000, CancellationToken.None);

        // Assert
        if (expected)
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
}
