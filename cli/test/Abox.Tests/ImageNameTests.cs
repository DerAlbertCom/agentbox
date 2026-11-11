using Abox.Naming;

namespace Abox.Tests;

public sealed class ImageNameTests
{
    [Fact]
    public void ALocalTeamReferenceRoundTrips()
    {
        ImageName
            .TryParseTeam(ImageName.OfTeam("other-team", "release"), "release", out var team)
            .Should()
            .BeTrue();

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
        ImageName.TryParseTeam(reference, "release", out var team).Should().BeFalse();

        team.Should().BeNull();
    }
}
