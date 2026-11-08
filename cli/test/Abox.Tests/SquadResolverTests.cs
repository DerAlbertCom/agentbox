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

        squad.Name.Should().Be("ac");
        squad.Workload.Name.Should().Be("mti-ca-dotnet");
        squad.Workload.Template.Should().Be("docker.io/sbx/shell-docker-image");
        var kit = squad.Kits.Should().ContainSingle().Which;
        kit.Name.Should().Be("pi-config");
        kit.IsLocal.Should().BeTrue();
        kit.Directory.Should().EndWith("kits/pi-config");
    }

    [Fact]
    public void TheNameOfASquadIsMatchedWithoutRegardToCase()
    {
        _root.File("ac.abox", "workload: mti-ca-dotnet\n");

        var squad = _resolver.Resolve(_root.Path, "AC", []);

        squad.Name.Should().Be("ac");
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

        squad.Kits.Select(kit => kit.Name).Should().Equal("pi-config", "other-kit");
        squad.ConfigFiles.Should().HaveCount(2);
    }

    [Fact]
    public void ACycleIsAnError()
    {
        _root.File("a.abox", "base: b\n");
        _root.File("b.abox", "base: a\n");

        var action = () => _resolver.Resolve(_root.Path, "a", []);

        action.Should().Throw<AboxException>().WithMessage("*cycle*");
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

        var action = () => _resolver.Resolve(_root.Path, "ac", []);

        action.Should().Throw<AboxException>().WithMessage("*declared twice*");
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

        var action = () => _resolver.Resolve(_root.Path, "ac", []);

        action.Should().Throw<AboxException>().WithMessage("*declared twice*");
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

        var action = () => _resolver.Resolve(_root.Path, "ac", []);

        action.Should().Throw<AboxException>().WithMessage("*declared twice*");
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

        var action = () => _resolver.Resolve(_root.Path, "ac", []);

        action.Should().Throw<AboxException>().WithMessage("*missing*");
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

        var kit = squad.Kits.Should().ContainSingle().Which;
        kit.Arguments["url"].Should().Be("https://example.com");
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

        var action = () => _resolver.Resolve(_root.Path, "ac", []);

        action.Should().Throw<AboxException>().WithMessage("*overlap*");
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

        var state = squad.State.Should().ContainSingle().Which;
        state.SandboxPath.Should().Be("/home/agent/.config/opencode/state");
        state.RelativePath.Should().Be(".config/opencode/state");
    }

    [Fact]
    public void ASquadWithoutAWorkloadIsAnError()
    {
        _root.File("ac.abox", "kits:\n  - pi-config\n");

        var action = () => _resolver.Resolve(_root.Path, "ac", []);

        action.Should().Throw<AboxException>().WithMessage("*workload*");
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

        var action = () => _resolver.Resolve(_root.Path, "ac", []);

        action.Should().Throw<AboxException>().WithMessage("*missing-kit*");
    }

    [Fact]
    public void ASquadIsFoundInAParentDirectory()
    {
        _root.File("ac.abox", "workload: mti-ca-dotnet\n");
        var folder = _root.Directory_("projects/one");

        var squad = _resolver.Resolve(folder, "ac", []);

        squad.Workload.Name.Should().Be("mti-ca-dotnet");
    }

    [Fact]
    public void TheBaseOfAPersonalConfigurationIsNotLookedForInAParentDirectory()
    {
        _root.File("ac.abox", "workload: mti-ca-dotnet\n");
        _root.File("base.abox", "kits:\n  - pi-config\n");
        var personal = _root.File("personal/personal.abox", "base: base\n");

        var action = () => _resolver.Resolve(_root.Path, "ac", [personal]);

        action.Should().Throw<AboxException>().WithMessage("*no configuration 'base.abox'*");
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

        squad.Kits.Select(kit => kit.Name).Should().Equal("pi-config", "personal-kit");
    }
}
