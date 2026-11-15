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
    [InlineData("agentbox-rwest-:release")]
    [InlineData("agentbox-rwest-sample:other")]
    [InlineData("registry.example/agentbox-rwest-sample:release")]
    [InlineData("agentbox-other-sample:release")]
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
