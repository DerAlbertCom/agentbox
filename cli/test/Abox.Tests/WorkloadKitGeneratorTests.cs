using Abox.Kits;

namespace Abox.Tests;

public sealed class WorkloadKitGeneratorTests
{
    private readonly TempDirectory _root = new();
    private readonly WorkloadKitGenerator _generator = new();

    [Fact]
    public void TheKitIsAWorkloadThatTheHostLaunchesAsAnAgent()
    {
        var workload = WriteWorkload(
            """
            template: docker.io/sbx/shell-docker-image
            mise:
              - nodejs@24.4.3
              - dotnet@10.0.403
            """
        );

        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        var descriptor = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.yaml"));
        descriptor.Should().Contain("# syntax=docker/sandbox-kit:3");
        descriptor.Should().Contain("schemaVersion: \"3\"");
        descriptor.Should().Contain("kind: workload");
        descriptor.Should().Contain("com.docker.sandbox/sbx@1");
    }

    [Fact]
    public void TheImageIsBuiltOnTheTemplateOfTheWorkload()
    {
        var workload = WriteWorkload("template: docker.io/sbx/shell-image\n");

        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile.Should().Contain("FROM docker.io/sbx/shell-image");
    }

    [Fact]
    public void AWorkloadWithoutATemplateUsesTheDefaultOne()
    {
        var workload = WriteWorkload("mise: []\n");

        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile.Should().Contain($"FROM {WorkloadFile.DefaultTemplate}");
    }

    [Fact]
    public void TheAptPackagesAreInstalledInOneStepBeforeTheTools()
    {
        var workload = WriteWorkload(
            """
            template: docker.io/sbx/shell-docker-image
            apt:
              - git
              - jq
            mise:
              - nodejs@24.4.3
            """
        );

        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile.Should().Contain("apt-get update");
        dockerfile.Should().Contain("apt-get install -y --no-install-recommends");
        dockerfile.Should().Contain("    git \\");
        dockerfile.Should().Contain("    jq \\");
        dockerfile.Should().Contain("apt-get clean");
        CountOccurrences(dockerfile, "apt-get update").Should().Be(1);
        dockerfile
            .IndexOf("jq", StringComparison.Ordinal)
            .Should()
            .BeLessThan(dockerfile.IndexOf("nodejs@24.4.3", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePackagesAreInstalledAsRootAndTheToolsAsTheAgentUser()
    {
        var workload = WriteWorkload(
            """
            apt:
              - git
            mise:
              - nodejs@24.4.3
            """
        );

        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile.Should().Contain("USER root\nRUN \\\n    apt-get update");
        dockerfile
            .Should()
            .Contain("lists/*\nUSER agent\n\n# mise manages the tools of this image");
        dockerfile
            .Should()
            .Contain("# mise manages the tools of this image, as the agent user.\nENV PATH=");
        CountOccurrences(dockerfile, "USER root").Should().Be(1);
        CountOccurrences(dockerfile, "USER agent").Should().Be(2);
    }

    [Fact]
    public void ANameThatIsNoPackageNameIsAnError()
    {
        var write = () =>
            WriteWorkload(
                """
                apt:
                  - "git; rm -rf /"
                """
            );

        write.Should().Throw<AboxException>().WithMessage("*not a package name*");
    }

    [Fact]
    public void EveryToolHasItsOwnInstallStep()
    {
        var workload = WriteWorkload(
            """
            mise:
              - nodejs@24.4.3
              - dotnet@10.0.403
            """
        );

        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile.Should().Contain("mise use -g -y nodejs@24.4.3");
        dockerfile.Should().Contain("mise use -g -y dotnet@10.0.403");
        CountOccurrences(dockerfile, "mise use -g -y").Should().Be(2);
    }

    [Fact]
    public void TheVersionsOfOneToolAreInstalledInOneStep()
    {
        var workload = WriteWorkload(
            """
            mise:
              - dotnet@9.0.303
              - dotnet@10.0.403
            """
        );

        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile.Should().Contain("mise use -g -y dotnet@10.0.403 dotnet@9.0.303");
        CountOccurrences(dockerfile, "mise use -g -y").Should().Be(1);
    }

    [Fact]
    public void TheVersionThatComesLastInTheWorkloadIsTheDefaultOfItsTool()
    {
        var workload = WriteWorkload(
            """
            mise:
              - dotnet@9.0.303
              - dotnet@10.0.403
            """
        );

        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile
            .IndexOf("dotnet@10.0.403", StringComparison.Ordinal)
            .Should()
            .BeLessThan(dockerfile.IndexOf("dotnet@9.0.303", StringComparison.Ordinal));
    }

    [Fact]
    public void TheToolsAreInstalledInReverseOrderSoTheLastOneIsTheDefault()
    {
        var workload = WriteWorkload(
            """
            mise:
              - nodejs@24.4.3
              - dotnet@10.0.403
            """
        );

        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile
            .IndexOf("dotnet@10.0.403", StringComparison.Ordinal)
            .Should()
            .BeLessThan(dockerfile.IndexOf("nodejs@24.4.3", StringComparison.Ordinal));
    }

    [Fact]
    public void TheScriptsRunAtTheEndOfTheBuild()
    {
        _root.File("enable-fzf.sh", "echo fzf");
        var workload = WriteWorkload(
            """
            scripts:
              - enable-fzf.sh
            """
        );

        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile.Should().Contain("COPY scripts/ /tmp/abox-scripts/");
        dockerfile.Should().Contain("RUN bash /tmp/abox-scripts/enable-fzf.sh");
        File.Exists(Path.Combine(_root.Path, "kit", "scripts", "enable-fzf.sh")).Should().BeTrue();
    }

    [Fact]
    public void TheWorkspaceIsTheWorkingDirectoryOfTheImage()
    {
        var workload = WriteWorkload("");

        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile.Should().Contain("WORKDIR /home/agent/workspace");
    }

    [Fact]
    public void TheGitHubTokenIsASecretAndNotAnArgument()
    {
        var workload = WriteWorkload(
            """
            mise:
              - nodejs@24.4.3
            """
        );

        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile
            .Should()
            .Contain($"--mount=type=secret,id={WorkloadKitGenerator.GitHubTokenSecret}");
        dockerfile.Should().NotContain("--build-arg");
    }

    [Fact]
    public void AScriptThatIsNotThereIsAnError()
    {
        var workload = WriteWorkload(
            """
            scripts:
              - missing.sh
            """
        );

        var action = () => _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        action.Should().Throw<AboxException>().WithMessage("*missing.sh*");
    }

    private WorkloadFile WriteWorkload(string content)
    {
        var path = _root.File("mti-ca-dotnet.workload", content);
        return WorkloadFile.Load(path);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
