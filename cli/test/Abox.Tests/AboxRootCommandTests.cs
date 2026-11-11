using System.CommandLine;
using Abox.Cli;
using Abox.Configuration;
using Abox.Features;
using Abox.Infrastructure;
using Abox.Launchers;
using Abox.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class AboxRootCommandTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheCompositionRootWiresVerbosityThroughToExternalTools(bool verbose)
    {
        using var root = new TempDirectory();
        var console = new TestConsole();
        var services = new ServiceCollection().AddAbox(console, new TrailingArguments());
        services.AddSingleton(AboxPaths.For(root.Path, root.Path));
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
    [InlineData("--help")]
    [InlineData("build", "--help")]
    [InlineData("secrets", "list", "--help")]
    [InlineData("exec", "sample")]
    [InlineData("pipe", "sample")]
    public async Task CommandsThatDoNotUseConfigurationWorkEvenWhenTheFileIsInvalid(
        params string[] arguments
    )
    {
        using var root = new TempDirectory();
        root.File("config.yaml", "unknown-key: invalid\n");
        var console = new TestConsole();
        var sbx = Substitute.For<ISbxClient>();
        sbx.ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>()).Returns(0);
        var services = new ServiceCollection().AddAbox(
            console,
            new TrailingArguments { Values = ["echo", "hello"] }
        );
        services.AddSingleton(AboxPaths.For(root.Path, Path.Combine(root.Path, "state")));
        services.AddSingleton(sbx);
        using var provider = services.BuildServiceProvider();

        var exitCode = await provider.GetRequiredService<AboxRootCommand>().InvokeAsync(arguments);

        exitCode.Should().Be(0);
        console.Errors.Should().BeEmpty();
        Directory.Exists(Path.Combine(root.Path, "state")).Should().BeFalse();
        if (arguments[0] is "exec" or "pipe")
        {
            await sbx.Received(1)
                .ExecAsync(
                    Arg.Is<SbxExecRequest>(request =>
                        request.Sandbox == "sample"
                        && request.Command.SequenceEqual(new[] { "echo", "hello" })
                        && request.KeepStandardInputOpen == (arguments[0] == "pipe")
                    ),
                    Arg.Any<CancellationToken>()
                );
        }
    }

    [Fact]
    public async Task TheExecutableIgnoresAppsettingsInTheWorkingDirectory()
    {
        using var root = new TempDirectory();
        root.File("appsettings.json", "not json");
        root.File("abox/config.yaml", "unknown-key: invalid\n");
        var result = await new ProcessRunner().RunAsync(
            new ProcessRequest(
                "dotnet",
                [typeof(AboxRootCommand).Assembly.Location, "--help"],
                WorkingDirectory: root.Path,
                Environment: new Dictionary<string, string>
                {
                    ["XDG_CONFIG_HOME"] = root.Path,
                    ["XDG_STATE_HOME"] = root.Path,
                }
            ),
            CancellationToken.None
        );

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("abox: AI agent sandboxes");
        result.StandardError.Should().BeEmpty();
    }

    [Theory]
    [InlineData("build")]
    [InlineData("create")]
    [InlineData("publish")]
    public async Task ConfigurationErrorsAreReportedOnceWhenACommandUsesIt(string command)
    {
        using var root = new TempDirectory();
        root.File("config.yaml", "unknown-key: invalid\n");
        var console = new TestConsole();
        var services = new ServiceCollection().AddAbox(console, new TrailingArguments());
        services.AddSingleton(AboxPaths.For(root.Path, root.Path));
        using var provider = services.BuildServiceProvider();

        var exitCode = await provider.GetRequiredService<AboxRootCommand>().InvokeAsync([command]);

        exitCode.Should().Be(1);
        console
            .Errors.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("error: ")
            .And.Contain("unknown-key");
    }

    [Fact]
    public async Task CancellationUsesTheConventionalExitCode()
    {
        var console = new TestConsole();

        var exitCode = await CommandHandling.RunAsync(
            () => throw new OperationCanceledException(),
            console
        );

        exitCode.Should().Be(130);
        console.Errors.Should().Equal("error: the command was cancelled.");
    }

    [Fact]
    public async Task DuplicateLauncherIdsAreReportedAsAnAboxErrorDuringComposition()
    {
        using var root = new TempDirectory();
        var console = new TestConsole();
        var services = new ServiceCollection().AddAbox(console, new TrailingArguments());
        services.AddSingleton(AboxPaths.For(root.Path, root.Path));
        services.AddSingleton<ILauncher, ShellLauncher>();

        var exitCode = await CommandHandling.RunAsync(
            async () =>
            {
                using var provider = services.BuildServiceProvider();
                return await provider.GetRequiredService<AboxRootCommand>().InvokeAsync(["--help"]);
            },
            console
        );

        exitCode.Should().Be(1);
        console
            .Errors.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("error: ")
            .And.Contain("shell")
            .And.Contain("duplicate");
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
