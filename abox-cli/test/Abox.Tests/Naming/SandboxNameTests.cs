using Abox.Naming;

namespace Abox.Tests;

public sealed class SandboxNameTests
{
    [Theory]
    [InlineData("my-project", "my-project")]
    [InlineData("My Project", "my-project")]
    [InlineData("__weird__", "weird")]
    [InlineData("a", "a")]
    [InlineData("", "folder")]
    [InlineData("...", "folder")]
    public void TheFolderNameHoldsOnlyLettersDigitsAndHyphens(string folder, string expected)
    {
        // Arrange: the theory supplies folder names requiring normalization.

        // Act
        var normalized = SandboxName.NormalizeFolderName(folder);

        // Assert
        normalized.Should().Be(expected);
    }

    [Fact]
    public void TheNameIsTheFolderAMarkerAndAHash()
    {
        // Arrange: use a fixed folder to inspect the generated identity.

        // Act
        var name = SandboxName.For("/home/albert/projects/rwe");

        // Assert
        name.Should().StartWith("rwe-abox-");
        name["rwe-abox-".Length..].Should().HaveLength(5);
        name["rwe-abox-".Length..].Should().MatchRegex("^[0-9A-F]{5}$");
    }

    [Fact]
    public void AnotherFolderGetsAnotherName()
    {
        // Arrange
        const string folder = "/home/albert/projects/rwe";
        const string otherFolder = "/home/albert/projects/other";

        // Act
        var name = SandboxName.For(folder);
        var otherName = SandboxName.For(otherFolder);

        // Assert
        name.Should().NotBe(otherName);
    }

    [Fact]
    public void TheNameFitsTheRulesOfSbx()
    {
        // Arrange: use a folder containing unsupported sandbox-name characters.

        // Act
        var name = SandboxName.For("/home/albert/projects/My Project!");

        // Assert
        name.Length.Should().BeGreaterThanOrEqualTo(2);
        char.IsAsciiLetterOrDigit(name[0]).Should().BeTrue();
        name.ToCharArray()
            .Should()
            .AllSatisfy(character =>
                (char.IsAsciiLetterOrDigit(character) || character is '-' or '.').Should().BeTrue()
            );
    }
}
