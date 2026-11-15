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
        // Arrange
        using var embedded = typeof(WorkloadKitGenerator).Assembly.GetManifestResourceStream(
            "Abox.Assets.rwe_inspection_certificate_chain.pem"
        );
        embedded.Should().NotBeNull("installed tools cannot depend on the repository or host PEM");
        using var source = new MemoryStream();
        embedded!.CopyTo(source);
        var expected = source.ToArray();
        // Security-integrity pin: trust-anchor changes require explicit approval, unlike static snapshots.
        Convert
            .ToHexString(SHA256.HashData(expected))
            .Should()
            .Be("5994104949F2CB3A35FCBA877D5D94E3123C394D05261044BE2CAB49C8B73BFC");

        // Act
        _generator.Generate(WriteWorkload(""), Path.Combine(_root.Path, "kit"));

        // Assert
        var bundle = File.ReadAllBytes(
            Path.Combine(_root.Path, "kit", "certificates", "rwe_inspection_certificate_chain.pem")
        );
        bundle.Should().Equal(expected);
    }

    [Fact]
    public void EachTrustedCertificateHasItsOwnCrtFileAndThereAreNoPrivateKeys()
    {
        // Arrange
        using var embedded = typeof(WorkloadKitGenerator).Assembly.GetManifestResourceStream(
            "Abox.Assets.rwe_inspection_certificate_chain.pem"
        );
        embedded.Should().NotBeNull();
        using var reader = new StreamReader(embedded!);
        var source = reader.ReadToEnd();
        var blocks = Regex.Matches(
            source,
            @"-----BEGIN CERTIFICATE-----[\s\S]+?-----END CERTIFICATE-----"
        );
        blocks.Should().NotBeEmpty();

        var directory = Path.Combine(_root.Path, "kit", "certificates");

        // Act
        _generator.Generate(WriteWorkload(""), Path.Combine(_root.Path, "kit"));

        // Assert
        File.ReadAllText(Path.Combine(directory, "rwe_inspection_certificate_chain.pem"))
            .Should()
            .NotContain("PRIVATE KEY");

        var files = Directory.GetFiles(directory, "*.crt").Order(StringComparer.Ordinal).ToList();
        files.Should().HaveCount(blocks.Count);
        for (var index = 0; index < files.Count; index++)
        {
            var pem = File.ReadAllText(files[index]);
            pem.Should().NotContain("PRIVATE KEY");
            pem.ReplaceLineEndings("\n")
                .Trim()
                .Should()
                .Be(blocks[index].Value.ReplaceLineEndings("\n"));
            using var certificate = X509Certificate2.CreateFromPem(pem);
            certificate.HasPrivateKey.Should().BeFalse();
        }

        // Arrange
        File.WriteAllText(Path.Combine(directory, "stale.crt"), "not an approved CA");

        // Act
        _generator.Generate(WriteWorkload(""), Path.Combine(_root.Path, "kit"));

        // Assert
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
        // Arrange
        _root.File("setup.sh", "echo setup");

        // Act
        _generator.Generate(WriteWorkload(configuration), Path.Combine(_root.Path, "kit"));

        // Assert
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
    public void TheImageIsBuiltOnTheTemplateOfTheWorkload()
    {
        // Arrange
        var workload = WriteWorkload("template: docker.io/sbx/shell-image\n");

        // Act
        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        // Assert
        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile.Should().Contain("FROM docker.io/sbx/shell-image");
    }

    [Fact]
    public void AWorkloadWithoutATemplateUsesTheDefaultOne()
    {
        // Arrange
        var workload = WriteWorkload("mise: []\n");

        // Act
        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        // Assert
        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile.Should().Contain($"FROM {WorkloadFile.DefaultTemplate}");
    }

    [Fact]
    public void TheAptPackagesAreInstalledInOneStepBeforeTheTools()
    {
        // Arrange
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

        // Act
        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        // Assert
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
        // Arrange
        var workload = WriteWorkload(
            """
            apt:
              - git
            mise:
              - nodejs@24.4.3
            """
        );

        // Act
        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        // Assert
        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile = dockerfile.ReplaceLineEndings("\n");
        foreach (
            var (instruction, user) in new[]
            {
                ("apt-get install", "root"),
                ("curl -fsSL https://mise.run", "agent"),
                ("mise use -g -y nodejs@24.4.3", "agent"),
            }
        )
        {
            var position = dockerfile.IndexOf(instruction, StringComparison.Ordinal);
            position.Should().BeGreaterThan(0);
            var activeUser = Regex
                .Matches(dockerfile[..position], @"(?m)^USER ([^\n]+)")
                .LastOrDefault()
                ?.Groups[1]
                .Value;
            activeUser.Should().Be(user, $"{instruction} must run as {user}");
        }
    }

    [Fact]
    public void ANameThatIsNoPackageNameIsAnError()
    {
        // Arrange: the workload text includes shell syntax in a package name.

        // Act
        var write = () =>
            WriteWorkload(
                """
                apt:
                  - "git; rm -rf /"
                """
            );

        // Assert
        write.Should().Throw<AboxException>().WithMessage("*not a package name*");
    }

    [Fact]
    public void EveryToolHasItsOwnInstallStep()
    {
        // Arrange
        var workload = WriteWorkload(
            """
            mise:
              - nodejs@24.4.3
              - dotnet@10.0.403
            """
        );

        // Act
        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        // Assert
        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile.Should().Contain("mise use -g -y nodejs@24.4.3");
        dockerfile.Should().Contain("mise use -g -y dotnet@10.0.403");
        CountOccurrences(dockerfile, "mise use -g -y").Should().Be(2);
    }

    [Fact(SkipExceptions = [typeof(ShellUnavailableException)])]
    public async Task AFailedMiseDownloadStopsTheBootstrap()
    {
        // Arrange: the synthetic shell functions make the download fail.

        // Act
        var result = await RunBootstrapAsync(
            """
            command() { return 1; }
            curl() { echo 'curl: (60) SSL certificate problem' >&2; return 60; }
            """
        );

        // Assert
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
        // Arrange: the synthetic installer succeeds without installing mise.

        // Act
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

        // Assert
        result
            .ExitCode.Should()
            .Be(127, "bootstrap must verify that mise is runnable before installing tools");
        result.StandardError.Should().Contain("mise: not found");
    }

    [Fact(SkipExceptions = [typeof(ShellUnavailableException)])]
    public async Task AFailedMiseInstallerStopsTheBootstrap()
    {
        // Arrange: the synthetic installer exits unsuccessfully.

        // Act
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

        // Assert
        result.ExitCode.Should().Be(23);
        result.StandardError.Should().NotContain("tools must not run");
    }

    [Fact(SkipExceptions = [typeof(ShellUnavailableException)])]
    public async Task AnExistingMiseDoesNotNeedADownload()
    {
        // Arrange: mise is available through a synthetic shell function.

        // Act
        var result = await RunBootstrapAsync(
            """
            curl() { echo 'download must not run' >&2; return 60; }
            mise() { echo 'mise test version'; }
            """
        );

        // Assert
        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("mise test version");
        result.StandardError.Should().NotContain("download must not run");
    }

    [Fact(SkipExceptions = [typeof(ShellUnavailableException)])]
    public async Task ANewMiseIsAvailableToTheNextRun()
    {
        // Arrange: the synthetic installer writes a runnable mise under HOME.

        // Act
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

        // Assert
        result.ExitCode.Should().Be(0, result.StandardError);
        result.StandardOutput.Should().Contain("mise --version");
        result.StandardOutput.Should().Contain("mise use -g -y dotnet@10.0.403");
    }

    [Fact]
    public void TheVersionsOfOneToolAreInstalledInOneStep()
    {
        // Arrange
        var workload = WriteWorkload(
            """
            mise:
              - dotnet@9.0.303
              - dotnet@10.0.403
            """
        );

        // Act
        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        // Assert
        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile.Should().Contain("mise use -g -y dotnet@10.0.403 dotnet@9.0.303");
        CountOccurrences(dockerfile, "mise use -g -y").Should().Be(1);
    }

    [Fact]
    public void TheVersionThatComesLastInTheWorkloadIsTheDefaultOfItsTool()
    {
        // Arrange
        var workload = WriteWorkload(
            """
            mise:
              - dotnet@9.0.303
              - dotnet@10.0.403
            """
        );

        // Act
        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        // Assert
        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile
            .IndexOf("dotnet@10.0.403", StringComparison.Ordinal)
            .Should()
            .BeLessThan(dockerfile.IndexOf("dotnet@9.0.303", StringComparison.Ordinal));
    }

    [Fact]
    public void TheToolsAreInstalledInReverseOrderSoTheLastOneIsTheDefault()
    {
        // Arrange
        var workload = WriteWorkload(
            """
            mise:
              - nodejs@24.4.3
              - dotnet@10.0.403
            """
        );

        // Act
        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        // Assert
        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile
            .IndexOf("dotnet@10.0.403", StringComparison.Ordinal)
            .Should()
            .BeLessThan(dockerfile.IndexOf("nodejs@24.4.3", StringComparison.Ordinal));
    }

    [Fact]
    public void TheScriptsRunAtTheEndOfTheBuild()
    {
        // Arrange
        _root.File("enable-fzf.sh", "echo fzf");
        var workload = WriteWorkload(
            """
            scripts:
              - enable-fzf.sh
            """
        );

        // Act
        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        // Assert
        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile.Should().Contain("COPY scripts/ /tmp/abox-scripts/");
        dockerfile.Should().Contain("RUN bash /tmp/abox-scripts/enable-fzf.sh");
        File.Exists(Path.Combine(_root.Path, "kit", "scripts", "enable-fzf.sh")).Should().BeTrue();
    }

    [Fact]
    public void GeneratedFilesUseLinuxLineEndings()
    {
        // Arrange
        _root.File("setup.sh", "#!/bin/bash\r\necho setup\r\n");

        // Act
        _generator.Generate(
            WriteWorkload("apt: [git]\nmise: [nodejs@24.4.3]\nscripts: [setup.sh]\n"),
            Path.Combine(_root.Path, "kit")
        );

        // Assert
        foreach (var file in new[] { "workload.dockerfile", "workload.yaml", "scripts/setup.sh" })
        {
            File.ReadAllText(Path.Combine(_root.Path, "kit", file)).Should().NotContain("\r");
        }
    }

    [Fact]
    public void ScriptCopyOnlyReplacesCrLfAndPreservesEveryOtherByte()
    {
        // Arrange
        var source = _root.File("setup.sh", "");
        byte[] content =
        [
            0x23,
            0x20,
            0xe9,
            0x0d,
            0x0a,
            0x61,
            0x0d,
            0x62,
            0x85,
            0x0c,
            0xe2,
            0x80,
            0xa8,
            0xe2,
            0x80,
            0xa9,
            0x0d,
        ];
        File.WriteAllBytes(source, content);

        // Act
        _generator.Generate(
            WriteWorkload("scripts: [setup.sh]\n"),
            Path.Combine(_root.Path, "kit")
        );

        // Assert
        File.ReadAllBytes(Path.Combine(_root.Path, "kit", "scripts", "setup.sh"))
            .Should()
            .Equal([
                0x23,
                0x20,
                0xe9,
                0x0a,
                0x61,
                0x0d,
                0x62,
                0x85,
                0x0c,
                0xe2,
                0x80,
                0xa8,
                0xe2,
                0x80,
                0xa9,
                0x0d,
            ]);
        File.ReadAllBytes(source).Should().Equal(content);
    }

    [Theory]
    [InlineData("setup.sh")]
    [InlineData("SETUP.SH")]
    public void ScriptsWithTheSameFileNameCannotOverwriteEachOther(string secondName)
    {
        // Arrange
        _root.File("first/setup.sh", "echo first\n");
        _root.File($"second/{secondName}", "echo second\n");
        var workload = WriteWorkload($"scripts: [first/setup.sh, second/{secondName}]\n");

        // Act
        var action = () => _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        // Assert
        action
            .Should()
            .Throw<AboxException>()
            .WithMessage($"*first/setup.sh*second/{secondName}*");
    }

    [Fact]
    public void TheGitHubTokenIsASecretAndNotAnArgument()
    {
        // Arrange
        var workload = WriteWorkload(
            """
            mise:
              - nodejs@24.4.3
            """
        );

        // Act
        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        // Assert
        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile
            .Should()
            .Contain($"--mount=type=secret,id={WorkloadKitGenerator.GitHubTokenSecret}");
        dockerfile.Should().NotContain("--build-arg");
    }

    [Fact]
    public void AScriptThatIsNotThereIsAnError()
    {
        // Arrange
        var workload = WriteWorkload(
            """
            scripts:
              - missing.sh
            """
        );

        // Act
        var action = () => _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        // Assert
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
