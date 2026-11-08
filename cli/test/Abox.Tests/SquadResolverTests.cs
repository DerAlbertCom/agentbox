using Abox.Configuration;
using Abox.Kits;

namespace Abox.Tests;

public sealed class SquadResolverTests
{
    private readonly TempDirectory _root = new();
    private readonly SquadResolver _resolver = new(new AboxConfig());

    public SquadResolverTests()
    {
        _root.Directory_("kits/pi-config");
        _root.File("kits/mti-ca-dotnet.workload", "template: docker.io/sbx/shell-docker-image\n");
    }

    [Fact]
    public void ASquadResolvesItsWorkloadAndItsKits()
    {
        _root.File(
            "ac.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - pi-config
            """
        );

        var squad = _resolver.Resolve(_root.Path, "ac", []);

        Assert.Equal("ac", squad.Name);
        Assert.Equal("mti-ca-dotnet", squad.Workload.Name);
        Assert.Equal("docker.io/sbx/shell-docker-image", squad.Workload.Template);
        var kit = Assert.Single(squad.Kits);
        Assert.Equal("pi-config", kit.Name);
        Assert.True(kit.IsLocal);
        Assert.EndsWith("kits/pi-config", kit.Directory);
    }

    [Fact]
    public void TheNameOfASquadIsMatchedWithoutRegardToCase()
    {
        _root.File("ac.abox", "workload: mti-ca-dotnet\n");

        var squad = _resolver.Resolve(_root.Path, "AC", []);

        Assert.Equal("ac", squad.Name);
    }

    [Fact]
    public void ABaseChainIsCombinedFromTheBaseToTheChild()
    {
        _root.File(
            "cape.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - pi-config
            """
        );
        _root.File(
            "ac.abox",
            """
            base: cape
            kits:
              - other-kit
            """
        );
        _root.Directory_("kits/other-kit");

        var squad = _resolver.Resolve(_root.Path, "ac", []);

        Assert.Equal(["pi-config", "other-kit"], squad.Kits.Select(kit => kit.Name));
        Assert.Equal(2, squad.ConfigFiles.Count);
    }

    [Fact]
    public void ACycleIsAnError()
    {
        _root.File("a.abox", "base: b\n");
        _root.File("b.abox", "base: a\n");

        var exception = Assert.Throws<AboxException>(() => _resolver.Resolve(_root.Path, "a", []));

        Assert.Contains("cycle", exception.Message);
    }

    [Fact]
    public void TheSameKitTwiceIsAnError()
    {
        _root.File(
            "cape.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - pi-config
            """
        );
        _root.File(
            "ac.abox",
            """
            base: cape
            kits:
              - pi-config
            """
        );

        var exception = Assert.Throws<AboxException>(() => _resolver.Resolve(_root.Path, "ac", []));

        Assert.Contains("declared twice", exception.Message);
    }

    [Fact]
    public void TwoTagsOfOneRepositoryAreOneKit()
    {
        _root.File(
            "ac.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - docker.io/acme/pi-config:1.0
              - docker.io/acme/pi-config:2.0
            """
        );

        var exception = Assert.Throws<AboxException>(() => _resolver.Resolve(_root.Path, "ac", []));

        Assert.Contains("declared twice", exception.Message);
    }

    [Fact]
    public void TheSameParameterTwiceIsAnError()
    {
        _root.File(
            "cape.abox",
            """
            workload: mti-ca-dotnet
            parameters:
              url: https://example.com
            """
        );
        _root.File(
            "ac.abox",
            """
            base: cape
            parameters:
              url: https://other.example.com
            """
        );

        var exception = Assert.Throws<AboxException>(() => _resolver.Resolve(_root.Path, "ac", []));

        Assert.Contains("declared twice", exception.Message);
    }

    [Fact]
    public void AParameterNoConfigurationDeclaresIsAnError()
    {
        _root.File(
            "ac.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - name: pi-config
                args:
                  url: ${{ parameters.missing }}
            """
        );

        var exception = Assert.Throws<AboxException>(() => _resolver.Resolve(_root.Path, "ac", []));

        Assert.Contains("missing", exception.Message);
    }

    [Fact]
    public void AParameterIsGivenToTheKitThatUsesIt()
    {
        _root.File(
            "ac.abox",
            """
            workload: mti-ca-dotnet
            parameters:
              url: https://example.com
            kits:
              - name: pi-config
                args:
                  url: ${{ parameters.url }}
            """
        );

        var squad = _resolver.Resolve(_root.Path, "ac", []);

        var kit = Assert.Single(squad.Kits);
        Assert.Equal("https://example.com", kit.Arguments["url"]);
    }

    [Fact]
    public void OverlappingStateDirectoriesAreAnError()
    {
        _root.File(
            "ac.abox",
            """
            workload: mti-ca-dotnet
            state:
              - ~/.config
              - ~/.config/opencode
            """
        );

        var exception = Assert.Throws<AboxException>(() => _resolver.Resolve(_root.Path, "ac", []));

        Assert.Contains("overlap", exception.Message);
    }

    [Fact]
    public void AStateDirectoryIsKeptOnce()
    {
        _root.File(
            "ac.abox",
            """
            workload: mti-ca-dotnet
            state:
              - ~/.config/opencode/state
            """
        );

        var squad = _resolver.Resolve(_root.Path, "ac", []);

        var state = Assert.Single(squad.State);
        Assert.Equal("/home/agent/.config/opencode/state", state.SandboxPath);
        Assert.Equal(".config/opencode/state", state.RelativePath);
    }

    [Fact]
    public void ASquadWithoutAWorkloadIsAnError()
    {
        _root.File("ac.abox", "kits:\n  - pi-config\n");

        var exception = Assert.Throws<AboxException>(() => _resolver.Resolve(_root.Path, "ac", []));

        Assert.Contains("workload", exception.Message);
    }

    [Fact]
    public void AKitThatIsNotThereIsAnError()
    {
        _root.File(
            "ac.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - missing-kit
            """
        );

        var exception = Assert.Throws<AboxException>(() => _resolver.Resolve(_root.Path, "ac", []));

        Assert.Contains("missing-kit", exception.Message);
    }

    [Fact]
    public void APersonalConfigurationAddsToTheSquad()
    {
        _root.File(
            "ac.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - pi-config
            """
        );
        var personal = _root.File(
            "personal.abox",
            """
            kits:
              - personal-kit
            """
        );
        _root.Directory_("kits/personal-kit");

        var squad = _resolver.Resolve(_root.Path, "ac", [personal]);

        Assert.Equal(["pi-config", "personal-kit"], squad.Kits.Select(kit => kit.Name));
    }
}
