using Abox.Sbx;
using YamlDotNet.RepresentationModel;

namespace Abox.Tests;

public sealed class SbxCredentialBindingsTests
{
    private readonly TempDirectory _root = new();

    private string BindingsFile => Path.Combine(_root.Path, "sbx", "credentials.yaml");

    [Fact]
    public void WithoutTheFileNoServiceIsApproved()
    {
        // Arrange
        var bindings = new SbxCredentialBindings(BindingsFile);

        // Act
        var approved = bindings.Covers("github", ["api.github.com"]);

        // Assert
        approved.Should().BeFalse();
        File.Exists(BindingsFile).Should().BeFalse();
    }

    [Fact]
    public void ABindingApprovesOnlyTheDomainsItLists()
    {
        // Arrange
        _root.File(
            "sbx/credentials.yaml",
            """
            bindings:
                github:
                    apiKey:
                        domains:
                            - api.github.com
            """
        );
        var bindings = new SbxCredentialBindings(BindingsFile);

        // Act
        var listed = bindings.Covers("github", ["api.github.com"]);
        var more = bindings.Covers("github", ["api.github.com", "uploads.github.com"]);
        var other = bindings.Covers("elastic", ["api.github.com"]);

        // Assert
        listed.Should().BeTrue();
        more.Should().BeFalse();
        other.Should().BeFalse();
    }

    [Fact]
    public void AnApprovalCreatesTheFile()
    {
        // Arrange
        var bindings = new SbxCredentialBindings(BindingsFile);

        // Act
        bindings.Approve("github", ["api.github.com"]);

        // Assert
        new SbxCredentialBindings(BindingsFile)
            .Covers("github", ["api.github.com"])
            .Should()
            .BeTrue();
        Domains(Load()["bindings"]["github"]).Should().Equal("api.github.com");
    }

    [Fact]
    public void AnApprovalKeepsEveryOtherEntry()
    {
        // Arrange
        _root.File(
            "sbx/credentials.yaml",
            """
            version: 2
            bindings:
                elastic:
                    apiKey:
                        domains:
                            - api.elastic.co
                github:
                    oauth:
                        scopes:
                            - repo
                    apiKey:
                        domains:
                            - api.github.com
            """
        );
        var bindings = new SbxCredentialBindings(BindingsFile);

        // Act
        bindings.Approve("github", ["uploads.github.com"]);

        // Assert
        var root = Load();
        ((YamlScalarNode)root["version"]).Value.Should().Be("2");
        Domains(root["bindings"]["elastic"]).Should().Equal("api.elastic.co");
        var scopes = (YamlSequenceNode)root["bindings"]["github"]["oauth"]["scopes"];
        scopes.Children.Should().ContainSingle().Which.ToString().Should().Be("repo");
        Domains(root["bindings"]["github"]).Should().Equal("api.github.com", "uploads.github.com");
    }

    [Fact]
    public void AFileThatIsNotABindingsMappingIsAnError()
    {
        // Arrange
        _root.File("sbx/credentials.yaml", "bindings: [github]\n");
        var bindings = new SbxCredentialBindings(BindingsFile);

        // Act
        var approve = () => bindings.Approve("github", ["api.github.com"]);

        // Assert
        approve.Should().Throw<AboxException>().WithMessage($"*{BindingsFile}*");
        File.ReadAllText(BindingsFile).Should().Be("bindings: [github]\n");
    }

    [Theory]
    [InlineData(false, null, "/home/user/.config/sbx/credentials.yaml")]
    [InlineData(false, "/xdg", "/xdg/sbx/credentials.yaml")]
    [InlineData(false, "relative", "/home/user/.config/sbx/credentials.yaml")]
    [InlineData(true, null, "/appdata/sbx/credentials.yaml")]
    public void TheFileIsWhereSbxKeepsIt(bool windows, string? xdgConfigHome, string expected)
    {
        // Arrange
        var appData = "/appdata";

        // Act
        var path = SbxCredentialBindings.ResolvePath(appData, xdgConfigHome, "/home/user", windows);

        // Assert
        path.Replace('\\', '/').Should().Be(expected);
    }

    private YamlMappingNode Load()
    {
        var stream = new YamlStream();
        using var reader = new StringReader(File.ReadAllText(BindingsFile));
        stream.Load(reader);
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static IEnumerable<string> Domains(YamlNode service) =>
        ((YamlSequenceNode)service["apiKey"]["domains"]).Children.Select(domain =>
            domain.ToString()
        );
}
