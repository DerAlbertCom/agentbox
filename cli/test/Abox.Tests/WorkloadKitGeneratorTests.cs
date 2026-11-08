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
        Assert.Contains("# syntax=docker/sandbox-kit:3", descriptor);
        Assert.Contains("schemaVersion: \"3\"", descriptor);
        Assert.Contains("kind: workload", descriptor);
        Assert.Contains("com.docker.sandbox/sbx@1", descriptor);
    }

    [Fact]
    public void TheImageIsBuiltOnTheTemplateOfTheWorkload()
    {
        var workload = WriteWorkload("template: docker.io/sbx/shell-image\n");

        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        Assert.Contains("FROM docker.io/sbx/shell-image", dockerfile);
    }

    [Fact]
    public void AWorkloadWithoutATemplateUsesTheDefaultOne()
    {
        var workload = WriteWorkload("mise: []\n");

        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        Assert.Contains($"FROM {WorkloadFile.DefaultTemplate}", dockerfile);
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
        Assert.Contains("mise use -g -y nodejs@24.4.3", dockerfile);
        Assert.Contains("mise use -g -y dotnet@10.0.403", dockerfile);
        Assert.Equal(2, CountOccurrences(dockerfile, "mise use -g -y"));
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
        Assert.Contains("mise use -g -y dotnet@10.0.403 dotnet@9.0.303", dockerfile);
        Assert.Equal(1, CountOccurrences(dockerfile, "mise use -g -y"));
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
        Assert.True(
            dockerfile.IndexOf("dotnet@10.0.403", StringComparison.Ordinal)
                < dockerfile.IndexOf("dotnet@9.0.303", StringComparison.Ordinal)
        );
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
        Assert.True(
            dockerfile.IndexOf("dotnet@10.0.403", StringComparison.Ordinal)
                < dockerfile.IndexOf("nodejs@24.4.3", StringComparison.Ordinal)
        );
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
        Assert.Contains("COPY scripts/ /tmp/abox-scripts/", dockerfile);
        Assert.Contains("RUN bash /tmp/abox-scripts/enable-fzf.sh", dockerfile);
        Assert.True(File.Exists(Path.Combine(_root.Path, "kit", "scripts", "enable-fzf.sh")));
    }

    [Fact]
    public void TheWorkspaceIsTheWorkingDirectoryOfTheImage()
    {
        var workload = WriteWorkload("");

        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        Assert.Contains("WORKDIR /home/agent/workspace", dockerfile);
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
        Assert.Contains(
            $"--mount=type=secret,id={WorkloadKitGenerator.GitHubTokenSecret}",
            dockerfile
        );
        Assert.DoesNotContain("--build-arg", dockerfile);
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

        var exception = Assert.Throws<AboxException>(() =>
            _generator.Generate(workload, Path.Combine(_root.Path, "kit"))
        );

        Assert.Contains("missing.sh", exception.Message);
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
