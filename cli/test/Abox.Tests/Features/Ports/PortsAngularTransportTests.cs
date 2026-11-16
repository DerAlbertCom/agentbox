using System.Text.Json;
using Abox.Cli;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Naming;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class PortsAngularTransportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AngularCommandUsesOnlySbxArgvAndDistinctRelaysAndReusesMappings(bool invalid)
    {
        // Arrange
        using var root = new TempDirectory();
        var location = root.Directory_("workspace");
        var name = SandboxName.For(location);
        root.File(
            "workspace/angular.json",
            """{"projects":{"web":{"architect":{"serve":{"options":{"port":4200}}}}}}"""
        );
        root.File(
            "workspace/package.json",
            invalid
                ? """{"scripts":{"start":"ng serve --port $PORT"}}"""
                : """{"scripts":{"start":"ng serve --port=4200"}}"""
        );
        var console = new TestConsole();
        var runner = Substitute.For<IProcessRunner>();
        var calls = new List<ProcessRequest>();
        var published = false;
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessRequest>();
                calls.Add(request);
                var args = request.Arguments;
                var output = args[0] switch
                {
                    "version" => """{"client":{"version":"0.51.0"}}""",
                    "diagnose" => """{"checks":[]}""",
                    "ls" => JsonSerializer.Serialize(new[] { new { name, status = "running" } }),
                    "exec" when args.Contains("python3") =>
                        """[{"application_port":4200,"relay_port":60044}]""",
                    "exec" => "",
                    "ports" when args.Contains("--publish") => Publish(),
                    "ports" => published
                        ? """[{"host_ip":"127.0.0.1","host_port":14200,"sandbox_port":60044,"protocol":"tcp4"}]"""
                        : "[]",
                    _ => throw new InvalidOperationException("Unexpected external operation"),
                };
                return new ProcessResult(0, output, "");
            });
        var programs = Substitute.For<IHostPrograms>();
        programs.Exists("sbx").Returns(true);
        using var provider = new ServiceCollection()
            .AddAbox(console, new TrailingArguments())
            .AddSingleton(new AboxConfig { DefaultLocation = location })
            .AddSingleton(AboxPaths.For(root.Path, root.Path))
            .AddSingleton(runner)
            .AddSingleton(programs)
            .BuildServiceProvider();

        // Act
        var first = await provider.GetRequiredService<AboxRootCommand>().InvokeAsync(["ports"]);
        var second = await provider.GetRequiredService<AboxRootCommand>().InvokeAsync(["ports"]);

        // Assert
        first.Should().Be(invalid ? 1 : 0);
        second.Should().Be(first);
        calls.Should().OnlyContain(call => call.FileName == "sbx" && !call.AttachConsole);
        if (invalid)
        {
            calls
                .Should()
                .NotContain(call => call.Arguments[0] == "exec" || call.Arguments[0] == "ports");
            console.AllText.Should().NotContain("Application ports published");
        }
        else
        {
            var publish = calls
                .Should()
                .ContainSingle(call => call.Arguments.Contains("--publish"))
                .Which;
            publish
                .Arguments.Should()
                .Equal("ports", name, "--publish", "127.0.0.1:14200:60044/tcp4", "--json");
            var relays = calls.Where(call => call.Arguments.Contains("python3")).ToArray();
            relays.Should().HaveCount(2);
            relays[0].Arguments.Skip(7).Should().Equal("4200", "--reserved");
            relays[1].Arguments.Skip(7).Should().Equal("4200", "--reserved", "60044");
            calls.Should().NotContain(call => call.Arguments.Contains("--unpublish"));
            console
                .AllText.Should()
                .Contain("2 occurrences")
                .And.Contain("Application ports published");
        }

        string Publish()
        {
            published = true;
            return "[]";
        }
    }
}
