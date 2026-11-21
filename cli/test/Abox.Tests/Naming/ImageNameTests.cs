using Abox.Naming;

namespace Abox.Tests;

public sealed class ImageNameTests
{
    [Fact]
    public void ALocalTeamReferenceRoundTrips()
    {
        // Arrange
        var reference = ImageName.OfTeam("other-team", "release");

        // Act
        var parsed = ImageName.TryParseTeam(reference, "release", out var team);

        // Assert
        parsed.Should().BeTrue();
        team.Should().Be("other-team");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("agentic-box-rwest-:release")]
    [InlineData("agentic-box-rwest-sample:other")]
    [InlineData("registry.example/agentic-box-rwest-sample:release")]
    [InlineData("agentic-box-other-sample:release")]
    public void OtherReferencesAreNotLocalTeamReferences(string? reference)
    {
        // Arrange: the theory supplies references outside the requested local identity.

        // Act
        var parsed = ImageName.TryParseTeam(reference, "release", out var team);

        // Assert
        parsed.Should().BeFalse();
        team.Should().BeNull();
    }
}
