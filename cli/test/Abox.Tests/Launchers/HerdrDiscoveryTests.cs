using Abox.Infrastructure;
using Abox.Launchers;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class HerdrDiscoveryTests
{
    [Theory]
    [InlineData("{\"machines\":[{\"name\":\"actual-other\"}]}")]
    [InlineData("{\"machines\":[{\"name\":\"ACTUAL\"}]}")]
    [InlineData("{\"machines\":[{\"name\":\"other\",\"target\":\"actual\"}]}")]
    [InlineData("{\"machines\":[]}")]
    public async Task OnlyAnExactMachineNamePreventsRegistration(string listing)
    {
        // Arrange
        var fixture = new HerdrProtocolFixture(listing);

        // Act
        await fixture.LaunchAsync();

        // Assert
        fixture.Calls.Should().Contain(r => r.FileName == "hc" && r.Arguments.Contains("add"));
    }

    [Theory]
    [InlineData(7, "{\"machines\":[]}")]
    [InlineData(0, "not json")]
    [InlineData(0, "[]")]
    [InlineData(0, "{}")]
    [InlineData(0, "{\"machines\":null}")]
    [InlineData(0, "{\"machines\":{}}")]
    [InlineData(0, "{\"machines\":[null]}")]
    [InlineData(0, "{\"machines\":[{\"name\":123}]}")]
    [InlineData(0, "{\"machines\":[{\"name\":\"actual\"},{}]}")]
    public async Task FailedOrMalformedDiscoveryNeverAddsAMachine(int exitCode, string listing)
    {
        // Arrange
        var fixture = new HerdrProtocolFixture(listing, exitCode);

        // Act
        var action = fixture.LaunchAsync;

        // Assert
        (await action.Should().ThrowAsync<AboxException>())
            .Which.Message.Should()
            .Contain("hc machine ls");
        fixture
            .Calls.Should()
            .NotContain(r => r.Arguments.Contains("add") || r.FileName == "herdr-app");
    }
}
