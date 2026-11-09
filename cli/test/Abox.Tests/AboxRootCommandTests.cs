using System.CommandLine;
using Abox.Cli;
using Abox.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Tests;

public sealed class AboxRootCommandTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheCompositionRootWiresVerbosityThroughToExternalTools(bool verbose)
    {
        var console = new TestConsole();
        var services = new ServiceCollection().AddAbox(console, new TrailingArguments());
        services.AddSingleton<Command>(provider =>
        {
            var command = new Command("test-tool");
            command.SetAction(
                async (_, cancellationToken) =>
                {
                    var result = await provider
                        .GetRequiredService<IProcessRunner>()
                        .RunAsync(
                            new ProcessRequest(
                                OperatingSystem.IsWindows() ? "cmd" : "sh",
                                OperatingSystem.IsWindows()
                                    ? ["/c", "echo tool-output & echo tool-error >&2 & exit /b 19"]
                                    : ["-c", "echo tool-output; echo tool-error >&2; exit 19"]
                            ),
                            cancellationToken
                        );
                    return result.ExitCode;
                }
            );
            return command;
        });
        using var provider = services.BuildServiceProvider();

        var exitCode = await provider
            .GetRequiredService<AboxRootCommand>()
            .InvokeAsync(verbose ? ["test-tool", "--verbose"] : ["test-tool"]);

        exitCode.Should().Be(19);
        console.Output.Should().BeEmpty();
        if (verbose)
        {
            console.AllText.Should().Contain("tool-output").And.Contain("tool-error");
        }
        else
        {
            console.AllText.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("--verbose", "build", "--team", "mti-ca")]
    [InlineData("build", "--verbose", "--team", "mti-ca")]
    [InlineData("build", "--team", "mti-ca", "--verbose")]
    [InlineData("secrets", "list", "--verbose")]
    public async Task VerboseIsAvailableBeforeOrAfterCommandsIncludingNestedCommands(
        params string[] args
    )
    {
        var console = new TestConsole();
        var output = new ProcessOutput(console);
        var build = new Command("build");
        build.Options.Add(new Option<string>("--team"));
        build.SetAction(_ => ShowOutput());
        var secrets = new Command("secrets");
        var list = new Command("list");
        list.SetAction(_ => ShowOutput());
        secrets.Subcommands.Add(list);
        var root = new AboxRootCommand([build, secrets], output);

        var exitCode = await root.InvokeAsync(args);

        exitCode.Should().Be(17);
        console.AllText.Should().Contain("complete stdout").And.Contain("complete stderr");
        console.Output.Should().BeEmpty();

        int ShowOutput()
        {
            output.Write(
                new ProcessRequest("tool", []),
                new ProcessResult(17, "complete stdout", "complete stderr")
            );
            return 17;
        }
    }

    [Fact]
    public async Task VerboseIsOptInForEachInvocation()
    {
        var output = new ProcessOutput(new TestConsole());
        var build = new Command("build");
        build.SetAction(_ => 0);
        var root = new AboxRootCommand([build], output);

        await root.InvokeAsync(["build", "--verbose"]);
        output.Verbose.Should().BeTrue();
        await root.InvokeAsync(["build"]);
        output.Verbose.Should().BeFalse();
    }

    [Fact]
    public async Task VerboseAfterTheSeparatorBelongsToTheChildNotAbox()
    {
        var output = new ProcessOutput(new TestConsole());
        var run = new Command("run");
        run.SetAction(_ => 0);
        var root = new AboxRootCommand([run], output);
        var (arguments, trailing) = CommandLine.Split(["run", "--", "--verbose"]);

        var exitCode = await root.InvokeAsync(arguments);

        exitCode.Should().Be(0);
        output.Verbose.Should().BeFalse();
        trailing.Should().Equal("--verbose");
    }
}
