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
        SandboxName.NormalizeFolderName(folder).Should().Be(expected);
    }

    [Fact]
    public void TheNameIsTheFolderAMarkerAndAHash()
    {
        var name = SandboxName.For("/home/albert/projects/rwe");

        name.Should().StartWith("rwe-abox-");
        name["rwe-abox-".Length..].Should().HaveLength(5);
        name["rwe-abox-".Length..].Should().MatchRegex("^[0-9A-F]{5}$");
    }

    [Fact]
    public void TheSameFolderAlwaysGetsTheSameName()
    {
        SandboxName
            .For("/home/albert/projects/rwe")
            .Should()
            .Be(SandboxName.For("/home/albert/projects/rwe"));
    }

    [Fact]
    public void AnotherFolderGetsAnotherName()
    {
        SandboxName
            .For("/home/albert/projects/rwe")
            .Should()
            .NotBe(SandboxName.For("/home/albert/projects/other"));
    }

    [Fact]
    public void TheNameFitsTheRulesOfSbx()
    {
        var name = SandboxName.For("/home/albert/projects/My Project!");

        name.Length.Should().BeGreaterThanOrEqualTo(2);
        char.IsAsciiLetterOrDigit(name[0]).Should().BeTrue();
        name.ToCharArray()
            .Should()
            .AllSatisfy(character =>
                (char.IsAsciiLetterOrDigit(character) || character is '-' or '.').Should().BeTrue()
            );
    }
}
