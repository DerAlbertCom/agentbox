using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Abox.Infrastructure;
using Abox.Kits;

namespace Abox.Tests;

public sealed class WorkloadKitGeneratorTests
{
    private readonly TempDirectory _root = new();
    private readonly WorkloadKitGenerator _generator = new();

    [Fact]
    public void TheApprovedCertificateBundleIsEmbeddedAndCopiedExactlyIntoTheBuildContext()
    {
        _generator.Generate(WriteWorkload(""), Path.Combine(_root.Path, "kit"));

        using var embedded = typeof(WorkloadKitGenerator).Assembly.GetManifestResourceStream(
            "Abox.Assets.rwe_inspection_certificate_chain.pem"
        );
        embedded.Should().NotBeNull("installed tools cannot depend on the repository or host PEM");
        const string expectedHash =
            "5994104949F2CB3A35FCBA877D5D94E3123C394D05261044BE2CAB49C8B73BFC";
        Convert.ToHexString(SHA256.HashData(embedded!)).Should().Be(expectedHash);
        var bundle = File.ReadAllBytes(
            Path.Combine(_root.Path, "kit", "certificates", "rwe_inspection_certificate_chain.pem")
        );
        Convert.ToHexString(SHA256.HashData(bundle)).Should().Be(expectedHash);
    }

    [Fact]
    public void EachTrustedCertificateHasItsOwnCrtFileAndThereAreNoPrivateKeys()
    {
        var directory = Path.Combine(_root.Path, "kit", "certificates");
        _generator.Generate(WriteWorkload(""), Path.Combine(_root.Path, "kit"));
        var bundle = File.ReadAllText(
            Path.Combine(directory, "rwe_inspection_certificate_chain.pem")
        );
        var blocks = Regex.Matches(
            bundle,
            @"-----BEGIN CERTIFICATE-----[\s\S]+?-----END CERTIFICATE-----"
        );
        blocks.Should().HaveCount(135);
        var remainder = Regex.Replace(
            bundle,
            @"-----BEGIN CERTIFICATE-----[\s\S]+?-----END CERTIFICATE-----",
            ""
        );
        remainder
            .Split('\n')
            .Should()
            .OnlyContain(line =>
                string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')
            );

        var files = Directory.GetFiles(directory, "*.crt").Order(StringComparer.Ordinal).ToList();
        files.Should().HaveCount(blocks.Count);
        for (var index = 0; index < files.Count; index++)
        {
            var pem = File.ReadAllText(files[index]);
            pem.ReplaceLineEndings("\n")
                .Trim()
                .Should()
                .Be(blocks[index].Value.ReplaceLineEndings("\n"));
            using var certificate = X509Certificate2.CreateFromPem(pem);
            certificate.HasPrivateKey.Should().BeFalse();
        }

        File.WriteAllText(Path.Combine(directory, "stale.crt"), "not an approved CA");
        _generator.Generate(WriteWorkload(""), Path.Combine(_root.Path, "kit"));
        File.Exists(Path.Combine(directory, "stale.crt")).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("apt: [git]\n")]
    [InlineData("mise: [nodejs@24.4.3]\n")]
    [InlineData("scripts: [setup.sh]\n")]
    public void CertificateTrustIsInstalledAsRootBeforeAnyDownloadsAndRetainedForTheAgent(
        string configuration
    )
    {
        _root.File("setup.sh", "echo setup");
        _generator.Generate(WriteWorkload(configuration), Path.Combine(_root.Path, "kit"));

        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"))
            .ReplaceLineEndings("\n");
        dockerfile
            .Should()
            .Contain(
                "USER root\nCOPY certificates/ /usr/local/share/ca-certificates/abox/\nRUN update-ca-certificates"
            );
        dockerfile
            .Should()
            .Contain(
                "ENV MISE_EXTRA_CA_CERTS=\"/usr/local/share/ca-certificates/abox/rwe_inspection_certificate_chain.pem\""
            );
        dockerfile
            .Should()
            .Contain(
                "ENV NODE_EXTRA_CA_CERTS=\"/usr/local/share/ca-certificates/abox/rwe_inspection_certificate_chain.pem\""
            );
        var trust = dockerfile.IndexOf("RUN update-ca-certificates", StringComparison.Ordinal);
        foreach (var download in new[] { "apt-get update", "curl -fsSL", "mise use", "RUN bash" })
        {
            var position = dockerfile.IndexOf(download, StringComparison.Ordinal);
            if (position >= 0)
            {
                trust.Should().BeLessThan(position);
            }
        }

        Regex.Matches(dockerfile, @"(?m)^USER ([^\n]+)")[^1].Groups[1].Value.Should().Be("agent");
        dockerfile.Should().NotContain("--insecure").And.NotContain("SSL_VERIFY=false");
        if (configuration.Length == 0 || configuration.StartsWith("apt:"))
        {
            dockerfile.Should().NotContain("mise.run");
        }
    }

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
        dockerfile = dockerfile.ReplaceLineEndings("\n");
        dockerfile.Should().Contain("USER root\nRUN \\\n    apt-get update");
        dockerfile
            .Should()
            .Contain("lists/*\nUSER agent\n\n# mise manages the tools of this image");
        dockerfile
            .Should()
            .Contain("# mise manages the tools of this image, as the agent user.\nENV PATH=");
        CountOccurrences(dockerfile, "USER root").Should().Be(2);
        CountOccurrences(dockerfile, "USER agent").Should().Be(3);
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

    [Fact(SkipExceptions = [typeof(ShellUnavailableException)])]
    public async Task AFailedMiseDownloadStopsTheBootstrap()
    {
        var result = await RunBootstrapAsync(
            """
            command() { return 1; }
            curl() { echo 'curl: (60) SSL certificate problem' >&2; return 60; }
            """
        );

        result.StandardError.Should().Contain("SSL certificate problem");
        result
            .ExitCode.Should()
            .Be(
                60,
                $"a failed download must not reach the next RUN without mise: {result.StandardError}"
            );
    }

    [Fact(SkipExceptions = [typeof(ShellUnavailableException)])]
    public async Task ASuccessfulInstallerMustLeaveMiseRunnable()
    {
        var result = await RunBootstrapAsync(
            """
            command() { return 1; }
            curl() {
                while [ "$1" != '-o' ]; do shift; done
                printf '%s\n' 'exit 0' > "$2"
            }
            mise() { echo 'mise: not found' >&2; return 127; }
            """
        );

        result
            .ExitCode.Should()
            .Be(127, "bootstrap must verify that mise is runnable before installing tools");
        result.StandardError.Should().Contain("mise: not found");
    }

    [Fact(SkipExceptions = [typeof(ShellUnavailableException)])]
    public async Task AFailedMiseInstallerStopsTheBootstrap()
    {
        var result = await RunBootstrapAsync(
            """
            command() { return 1; }
            curl() {
                while [ "$1" != '-o' ]; do shift; done
                printf '%s\n' 'exit 23' > "$2"
            }
            mise() { echo 'tools must not run' >&2; return 0; }
            """
        );

        result.ExitCode.Should().Be(23);
        result.StandardError.Should().NotContain("tools must not run");
    }

    [Fact(SkipExceptions = [typeof(ShellUnavailableException)])]
    public async Task AnExistingMiseDoesNotNeedADownload()
    {
        var result = await RunBootstrapAsync(
            """
            curl() { echo 'download must not run' >&2; return 60; }
            mise() { echo 'mise test version'; }
            """
        );

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("mise test version");
        result.StandardError.Should().NotContain("download must not run");
    }

    [Fact(SkipExceptions = [typeof(ShellUnavailableException)])]
    public async Task ANewMiseIsAvailableToTheNextRun()
    {
        var result = await RunBootstrapAsync(
            """
            command() { return 1; }
            curl() {
                while [ "$1" != '-o' ]; do shift; done
                cat > "$2" <<'INSTALLER'
            mkdir -p "$HOME/.local/bin"
            printf '%s\n' '#!/bin/sh' 'echo "mise $*"' > "$HOME/.local/bin/mise"
            chmod +x "$HOME/.local/bin/mise"
            INSTALLER
            }
            """,
            "sh -c 'GITHUB_TOKEN= mise use -g -y dotnet@10.0.403'"
        );

        result.ExitCode.Should().Be(0, result.StandardError);
        result.StandardOutput.Should().Contain("mise --version");
        result.StandardOutput.Should().Contain("mise use -g -y dotnet@10.0.403");
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
    public void GeneratedFilesUseLinuxLineEndings()
    {
        _root.File("setup.sh", "#!/bin/bash\r\necho setup\r\n");
        _generator.Generate(
            WriteWorkload("apt: [git]\nmise: [nodejs@24.4.3]\nscripts: [setup.sh]\n"),
            Path.Combine(_root.Path, "kit")
        );

        foreach (var file in new[] { "workload.dockerfile", "workload.yaml", "scripts/setup.sh" })
        {
            File.ReadAllText(Path.Combine(_root.Path, "kit", file)).Should().NotContain("\r");
        }
    }

    [Theory]
    [InlineData("setup.sh")]
    [InlineData("SETUP.SH")]
    public void ScriptsWithTheSameFileNameCannotOverwriteEachOther(string secondName)
    {
        _root.File("first/setup.sh", "echo first\n");
        _root.File($"second/{secondName}", "echo second\n");
        var workload = WriteWorkload($"scripts: [first/setup.sh, second/{secondName}]\n");

        var action = () => _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        action.Should().Throw<AboxException>().WithMessage($"*first/setup.sh*second/{secondName}*");
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

    private async Task<ProcessResult> RunBootstrapAsync(string commands, string afterBootstrap = "")
    {
        var shell = "sh";
        if (OperatingSystem.IsWindows())
        {
            shell =
                (Environment.GetEnvironmentVariable("PATH") ?? "")
                    .Split(Path.PathSeparator)
                    .Where(directory => File.Exists(Path.Combine(directory, "git.exe")))
                    .Select(directory =>
                        Path.GetFullPath(Path.Combine(directory, "..", "usr", "bin", "sh.exe"))
                    )
                    .FirstOrDefault(File.Exists)
                ?? throw new ShellUnavailableException(
                    "Git for Windows sh is needed to execute Docker RUN commands."
                );
        }

        _generator.Generate(
            WriteWorkload("mise: [dotnet@10.0.403]\n"),
            Path.Combine(_root.Path, "kit")
        );
        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        var bootstrap = Regex.Match(dockerfile, @"(?m)^RUN set -eu;(?:[^\n]*\\\r?\n)*[^\n]*").Value;
        bootstrap.Should().StartWith("RUN ");
        var script =
            "HOME=\"$PWD\"; export HOME; PATH=\"$HOME/.local/bin:/usr/bin:/bin\"; export PATH\n"
            + commands
            + "\n"
            + Regex.Replace(bootstrap[4..], @"\\\r?\n", "\n")
            + "\n"
            + afterBootstrap;

        return await new ProcessRunner().RunAsync(
            new ProcessRequest(shell, ["-c", script], WorkingDirectory: _root.Path),
            CancellationToken.None
        );
    }

    private sealed class ShellUnavailableException(string message) : Exception(message);

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
