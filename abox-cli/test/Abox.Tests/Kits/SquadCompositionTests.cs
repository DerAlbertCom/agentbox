using Abox.Configuration;
using Abox.Kits;

namespace Abox.Tests;

public sealed class SquadCompositionTests
{
    [Theory]
    [InlineData("kits: [{name: example.com/kit:1, args: {url: '${{ parameters.missing }}'}}]")]
    [InlineData("mcp: [{name: elastic, url: '${{ parameters.missing }}'}]")]
    public void UndeclaredParametersIdentifyThePersonalFileThatUsesThem(string declaration)
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("teams/sample/base.workload", "template: example.com/base:1\n");
        root.File("teams/sample/ac.abox", "workload: base\n");
        var personal = root.File("personal/extra.abox", declaration + "\n");
        var resolver = new SquadResolver(new AboxConfig());

        // Act
        var resolve = () =>
            resolver.Resolve(root.Path, "sample", "ac", [Path.GetFullPath(personal)]);

        // Assert
        resolve
            .Should()
            .Throw<AboxException>()
            .WithMessage(
                $"'{Path.GetFullPath(personal)}' uses the parameter 'missing', which no configuration declares."
            );
    }

    [Theory]
    [InlineData("base")]
    [InlineData("./base")]
    public void PersonalChainsFollowTheProjectChainAndCanSupplyParametersUsedByEarlierFiles(
        string baseName
    )
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("teams/sample/base.workload", "template: example.com/base:1\n");
        var projectBase = root.File(
            "teams/sample/base.abox",
            "workload: base\nkits: [{name: example.com/base-kit:1, args: {url: '${{ parameters.url }}'}}]\nstate: [~/.base]\n"
        );
        var projectChild = root.File(
            "teams/sample/ac.abox",
            "base: base\nparameters: {name: child}\nmcp: [{name: elastic, url: '${{ parameters.url }}/mcp'}]\nstate: [~/.child]\n"
        );
        var personalBase = root.File(
            "personal/base.abox",
            "kits: [{name: personal-kit, args: {name: '${{ parameters.name }}'}}]\nstate: [~/.personal-base]\n"
        );
        var personalChild = root.File(
            "personal/extra.abox",
            $"base: {baseName}\nparameters: {{url: 'https://example.com'}}\nkits: [example.com/extra:1]\nstate: [~/.personal-child]\n"
        );
        var localKit = root.CreateDirectory("kits/personal-kit");
        var resolver = new SquadResolver(new AboxConfig());
        var files = new[] { projectBase, projectChild, personalBase, personalChild };

        // Act
        var squad = resolver.Resolve(root.Path, "sample", "AC", [personalBase, personalChild]);

        // Assert
        squad.Name.Should().Be("ac");
        squad.ConfigFiles.Should().Equal(files.Select(Path.GetFullPath));
        squad
            .Kits.Select(kit => kit.Name)
            .Should()
            .Equal("example.com/base-kit:1", "personal-kit", "example.com/extra:1");
        squad.Kits[0].Arguments["url"].Should().Be("https://example.com");
        squad.Kits[1].Arguments["name"].Should().Be("child");
        squad.Kits[1].Directory.Should().Be(Path.GetFullPath(localKit));
        squad.Mcp.Should().Equal(new McpDeclaration("elastic", "https://example.com/mcp"));
        squad
            .State.Select(entry => entry.RelativePath)
            .Should()
            .Equal(".base", ".child", ".personal-base", ".personal-child");
    }

    [Theory]
    [InlineData(
        "parameters: {url: first}",
        "parameters: {url: second}",
        "parameter 'url'",
        "A parameter is declared once."
    )]
    [InlineData(
        "kits: [example.com/kit:1]",
        "kits: [example.com/kit:2]",
        "kit 'example.com/kit:2'",
        "One repository is used once."
    )]
    [InlineData(
        "mcp: [elastic]",
        "mcp: [elastic]",
        "MCP server 'elastic'",
        "A server is declared once."
    )]
    [InlineData(
        "state: [~/.cache]",
        "state: [/home/agent/.cache/]",
        "state directory '/home/agent/.cache/'",
        "A state directory is declared once."
    )]
    [InlineData(
        "secrets: [{service: github, prompt: Token}]",
        "secrets: [{service: github, prompt: Other}]",
        "secret 'github'",
        "A secret is declared once."
    )]
    public void PersonalDuplicatesIdentifyBothOriginsAndTheDeclarationRule(
        string original,
        string duplicate,
        string declaration,
        string rule
    )
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("teams/sample/base.workload", "template: example.com/base:1\n");
        var project = root.File("teams/sample/ac.abox", $"workload: base\n{original}\n");
        var personal = root.File("personal/extra.abox", duplicate + "\n");
        var resolver = new SquadResolver(new AboxConfig());

        // Act
        var resolve = () =>
            resolver.Resolve(root.Path, "sample", "ac", [Path.GetFullPath(personal)]);

        // Assert
        resolve
            .Should()
            .Throw<AboxException>()
            .WithMessage(
                $"The {declaration} is declared twice: in '{Path.GetFullPath(project)}' and in '{Path.GetFullPath(personal)}'. {rule}"
            );
    }

    [Theory]
    [InlineData("personal/base.abox")]
    [InlineData("personal/extra.abox")]
    [InlineData("teams/sample/child.abox")]
    public void OnlyTheProjectRootMayOwnAWorkloadAndPersonalViolationsAreReportedFirst(
        string offender
    )
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("teams/sample/base.abox", "kits: []\n");
        root.File("teams/sample/child.abox", "base: base\nworkload: child\n");
        root.File("personal/base.abox", "kits: []\n");
        var personal = root.File("personal/extra.abox", "base: base\n");
        root.File(
            offender,
            offender.EndsWith("base.abox")
                ? "workload: forbidden\n"
                : "base: base\nworkload: forbidden\n"
        );
        var resolver = new SquadResolver(new AboxConfig());

        // Act
        var resolve = () =>
            resolver.Resolve(root.Path, "sample", "child", [Path.GetFullPath(personal)]);

        // Assert
        resolve
            .Should()
            .Throw<AboxException>()
            .WithMessage(
                $"'{Path.GetFullPath(Path.Combine(root.Path, offender))}' declares a workload. Only the root configuration of a squad declares one."
            );
    }
}
