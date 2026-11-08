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
        Assert.Equal(expected, SandboxName.NormalizeFolderName(folder));
    }

    [Fact]
    public void TheNameIsTheFolderAMarkerAndAHash()
    {
        var name = SandboxName.For("/home/albert/projects/rwe");

        Assert.StartsWith("rwe-abox-", name);
        Assert.Equal(5, name["rwe-abox-".Length..].Length);
        Assert.Matches("^[0-9A-F]{5}$", name["rwe-abox-".Length..]);
    }

    [Fact]
    public void TheSameFolderAlwaysGetsTheSameName()
    {
        Assert.Equal(SandboxName.For("/home/albert/projects/rwe"), SandboxName.For("/home/albert/projects/rwe"));
    }

    [Fact]
    public void AnotherFolderGetsAnotherName()
    {
        Assert.NotEqual(SandboxName.For("/home/albert/projects/rwe"), SandboxName.For("/home/albert/projects/other"));
    }

    [Fact]
    public void TheNameFitsTheRulesOfSbx()
    {
        var name = SandboxName.For("/home/albert/projects/My Project!");

        Assert.True(name.Length >= 2);
        Assert.True(char.IsAsciiLetterOrDigit(name[0]));
        Assert.All(name, character => Assert.True(
            char.IsAsciiLetterOrDigit(character) || character is '-' or '.'));
    }
}
