using Abox.Configuration;
using Abox.Kits;

namespace Abox.Tests;

public sealed class SquadResolverTests : IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly SquadResolver _resolver = new(new AboxConfig());

    public void Dispose() => _root.Dispose();

    public SquadResolverTests()
    {
        _root.Directory_("kits/pi-config");
        _root.File(
            "teams/sample/mti-ca-dotnet.workload",
            "template: docker.io/sbx/shell-docker-image\n"
        );
    }

    [Fact]
    public void ASquadIsResolvedInsideItsTeam()
    {
        // Arrange
        _root.File("teams/sample/ac.abox", "workload: mti-ca-dotnet\n");
        _root.File("teams/other/ac.abox", "base: cape\n");

        // Act
        var squad = _resolver.Resolve(_root.Path, "sample", "AC", []);

        // Assert
        squad.Workload.Name.Should().Be("mti-ca-dotnet");
    }

    [Fact]
    public void ABaseOfTheSameTeamWinsOverTheOtherTeams()
    {
        // Arrange
        _root.File("teams/other/ac.abox", "base: cape\n");
        _root.File("teams/sample/cape.abox", "workload: sample\n");
        _root.File("teams/other/mti-ca.workload", "template: docker.io/sbx/shell-image\n");
        _root.File("teams/other/cape.abox", "workload: mti-ca\n");

        // Act
        var squad = _resolver.Resolve(_root.Path, "other", "ac", []);

        // Assert
        squad.Workload.Name.Should().Be("mti-ca");
    }

    [Fact]
    public void ABaseThatSeveralOtherTeamsHoldIsAnError()
    {
        // Arrange
        _root.File("teams/other/ac.abox", "base: cape\n");
        _root.File("teams/sample/cape.abox", "workload: sample\n");
        _root.File("teams/third/cape.abox", "workload: mti-ca\n");

        // Act
        var action = () => _resolver.Resolve(_root.Path, "other", "ac", []);

        // Assert
        action.Should().Throw<AboxException>().WithMessage("*several team folders*");
    }

    [Fact]
    public void ATeamIsNamedByItsSquadsThatNothingElseUsesAsABase()
    {
        // Arrange
        _root.File("teams/sample/cape.abox", "workload: mti-ca-dotnet\n");
        _root.File("teams/sample/ac.abox", "base: cape\n");

        // Act
        var team = _resolver.ResolveTeam(_root.Path, "sample");

        // Assert
        team.Name.Should().Be("sample");
        team.Folder.Should().EndWith("sample");
        team.Squads.Should().Equal("ac");
    }

    [Fact]
    public void TheNameOfATeamIsMatchedWithoutRegardToCase()
    {
        // Arrange
        _root.File("teams/sample/ac.abox", "workload: mti-ca-dotnet\n");

        // Act
        var team = _resolver.ResolveTeam(_root.Path, "SAMPLE");

        // Assert
        team.Name.Should().Be("sample");
        team.Squads.Should().Equal("ac");
    }

    [Fact]
    public void ATeamFolderWithoutSquadsIsAnError()
    {
        // Arrange
        _root.Directory_("teams/sample");

        // Act
        var action = () => _resolver.ResolveTeam(_root.Path, "sample");

        // Assert
        action.Should().Throw<AboxException>().WithMessage("*no squads*");
    }

    [Fact]
    public void SquadsThatAllAreBasesAreAnError()
    {
        // Arrange
        _root.File("teams/sample/one.abox", "base: one\n");

        // Act
        var action = () => _resolver.ResolveTeam(_root.Path, "sample");

        // Assert
        action.Should().Throw<AboxException>().WithMessage("*base of another one*");
    }

    [Fact]
    public void ATeamThatIsNotThereIsAnError()
    {
        // Arrange: the fixture has no team named missing.

        // Act
        var action = () => _resolver.ResolveTeam(_root.Path, "missing");

        // Assert
        action.Should().Throw<AboxException>().WithMessage("*There is no team 'missing'*");
    }

    [Fact]
    public void ASquadResolvesItsWorkloadAndItsKits()
    {
        // Arrange
        _root.File(
            "teams/sample/ac.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - pi-config
            """
        );

        // Act
        var squad = _resolver.Resolve(_root.Path, "sample", "ac", []);

        // Assert
        squad.Name.Should().Be("ac");
        squad.Workload.Name.Should().Be("mti-ca-dotnet");
        squad.Workload.Template.Should().Be("docker.io/sbx/shell-docker-image");
        var kit = squad.Kits.Should().ContainSingle().Which;
        kit.Name.Should().Be("pi-config");
        kit.IsLocal.Should().BeTrue();
        kit.Directory.Should().EndWith(Path.Combine("kits", "pi-config"));
    }

    [Fact]
    public void TheNameOfASquadIsMatchedWithoutRegardToCase()
    {
        // Arrange
        _root.File("teams/sample/ac.abox", "workload: mti-ca-dotnet\n");

        // Act
        var squad = _resolver.Resolve(_root.Path, "sample", "AC", []);

        // Assert
        squad.Name.Should().Be("ac");
    }

    [Fact]
    public void ABaseChainIsCombinedFromTheBaseToTheChild()
    {
        // Arrange
        _root.File(
            "teams/sample/cape.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - pi-config
            """
        );
        _root.File(
            "teams/sample/ac.abox",
            """
            base: cape
            kits:
              - other-kit
            """
        );
        _root.Directory_("kits/other-kit");

        // Act
        var squad = _resolver.Resolve(_root.Path, "sample", "ac", []);

        // Assert
        squad.Kits.Select(kit => kit.Name).Should().Equal("pi-config", "other-kit");
        squad.ConfigFiles.Should().HaveCount(2);
    }

    [Fact]
    public void ACycleIsAnError()
    {
        // Arrange
        _root.File("teams/sample/a.abox", "base: b\n");
        _root.File("teams/sample/b.abox", "base: a\n");

        // Act
        var action = () => _resolver.Resolve(_root.Path, "sample", "a", []);

        // Assert
        action.Should().Throw<AboxException>().WithMessage("*cycle*");
    }

    [Fact]
    public void TheSameKitTwiceIsAnError()
    {
        // Arrange
        _root.File(
            "teams/sample/cape.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - pi-config
            """
        );
        _root.File(
            "teams/sample/ac.abox",
            """
            base: cape
            kits:
              - pi-config
            """
        );

        // Act
        var action = () => _resolver.Resolve(_root.Path, "sample", "ac", []);

        // Assert
        action.Should().Throw<AboxException>().WithMessage("*declared twice*");
    }

    [Fact]
    public void TwoTagsOfOneRepositoryAreOneKit()
    {
        // Arrange
        _root.File(
            "teams/sample/ac.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - docker.io/acme/pi-config:1.0
              - docker.io/acme/pi-config:2.0
            """
        );

        // Act
        var action = () => _resolver.Resolve(_root.Path, "sample", "ac", []);

        // Assert
        action.Should().Throw<AboxException>().WithMessage("*declared twice*");
    }

    [Fact]
    public void TheSameParameterTwiceIsAnError()
    {
        // Arrange
        _root.File(
            "teams/sample/cape.abox",
            """
            workload: mti-ca-dotnet
            parameters:
              url: https://example.com
            """
        );
        _root.File(
            "teams/sample/ac.abox",
            """
            base: cape
            parameters:
              url: https://other.example.com
            """
        );

        // Act
        var action = () => _resolver.Resolve(_root.Path, "sample", "ac", []);

        // Assert
        action.Should().Throw<AboxException>().WithMessage("*declared twice*");
    }

    [Fact]
    public void AParameterNoConfigurationDeclaresIsAnError()
    {
        // Arrange
        _root.File(
            "teams/sample/ac.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - name: pi-config
                args:
                  url: ${{ parameters.missing }}
            """
        );

        // Act
        var action = () => _resolver.Resolve(_root.Path, "sample", "ac", []);

        // Assert
        action.Should().Throw<AboxException>().WithMessage("*missing*");
    }

    [Fact]
    public void AParameterIsGivenToTheKitThatUsesIt()
    {
        // Arrange
        _root.File(
            "teams/sample/ac.abox",
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

        // Act
        var squad = _resolver.Resolve(_root.Path, "sample", "ac", []);

        // Assert
        var kit = squad.Kits.Should().ContainSingle().Which;
        kit.Arguments["url"].Should().Be("https://example.com");
    }

    [Fact]
    public void OverlappingStateDirectoriesAreAnError()
    {
        // Arrange
        _root.File(
            "teams/sample/ac.abox",
            """
            workload: mti-ca-dotnet
            state:
              - ~/.config
              - ~/.config/opencode
            """
        );

        // Act
        var action = () => _resolver.Resolve(_root.Path, "sample", "ac", []);

        // Assert
        action.Should().Throw<AboxException>().WithMessage("*overlap*");
    }

    [Fact]
    public void AStateDirectoryIsKeptOnce()
    {
        // Arrange
        _root.File(
            "teams/sample/ac.abox",
            """
            workload: mti-ca-dotnet
            state:
              - ~/.config/opencode/state
            """
        );

        // Act
        var squad = _resolver.Resolve(_root.Path, "sample", "ac", []);

        // Assert
        var state = squad.State.Should().ContainSingle().Which;
        state.SandboxPath.Should().Be("/home/agent/.config/opencode/state");
        state.RelativePath.Should().Be(".config/opencode/state");
    }

    [Fact]
    public void ASquadWithoutAWorkloadIsAnError()
    {
        // Arrange
        _root.File("teams/sample/ac.abox", "kits:\n  - pi-config\n");

        // Act
        var action = () => _resolver.Resolve(_root.Path, "sample", "ac", []);

        // Assert
        action.Should().Throw<AboxException>().WithMessage("*workload*");
    }

    [Fact]
    public void AKitThatIsNotThereIsAnError()
    {
        // Arrange
        _root.File(
            "teams/sample/ac.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - missing-kit
            """
        );

        // Act
        var action = () => _resolver.Resolve(_root.Path, "sample", "ac", []);

        // Assert
        action.Should().Throw<AboxException>().WithMessage("*missing-kit*");
    }

    [Fact]
    public void ASquadIsFoundInAParentDirectory()
    {
        // Arrange
        _root.File("teams/sample/ac.abox", "workload: mti-ca-dotnet\n");
        var folder = _root.Directory_("projects/one");

        // Act
        var squad = _resolver.Resolve(folder, "sample", "ac", []);

        // Assert
        squad.Workload.Name.Should().Be("mti-ca-dotnet");
    }

    [Fact]
    public void TheBaseOfAPersonalConfigurationIsNotLookedForInAParentDirectory()
    {
        // Arrange
        _root.File("teams/sample/ac.abox", "workload: mti-ca-dotnet\n");
        _root.File("base.abox", "kits:\n  - pi-config\n");
        var personal = _root.File("personal/personal.abox", "base: base\n");

        // Act
        var action = () => _resolver.Resolve(_root.Path, "sample", "ac", [personal]);

        // Assert
        action.Should().Throw<AboxException>().WithMessage("*no configuration 'base.abox'*");
    }

    [Fact]
    public void APersonalConfigurationAddsToTheSquad()
    {
        // Arrange
        _root.File(
            "teams/sample/ac.abox",
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

        // Act
        var squad = _resolver.Resolve(_root.Path, "sample", "ac", [personal]);

        // Assert
        squad.Kits.Select(kit => kit.Name).Should().Equal("pi-config", "personal-kit");
    }
}
