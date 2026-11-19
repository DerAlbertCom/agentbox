namespace Abox.Tests;

public sealed class UserSecretsSelectionUnionCommandTests
{
    public static TheoryData<string, string> Selections =>
        new()
        {
            { """[{"id":"Cape.*","keys":[]},{"id":"cape.one","keys":["A"]}]""", """{"A":1}""" },
            { """[{"id":"Cape.*","keys":["A"]},{"id":"cape.one","keys":[]}]""", """{"A":1}""" },
            {
                """[{"id":"Cape.*","keys":[]},"cape.one",{"id":"Cape.o*","keys":["A"]}]""",
                """{"A":1,"B":2}"""
            },
            {
                """[{"id":"Cape.*","keys":["A"]},"cape.one",{"id":"Cape.o*","keys":[]}]""",
                """{"A":1,"B":2}"""
            },
        };

    [Theory]
    [MemberData(nameof(Selections))]
    public async Task EmptyFiltersDoNotWidenAUnionAndALaterWholeStoreCannotBeNarrowed(
        string selections,
        string expected
    )
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        fixture.Configure($$"""{"userSecrets":{{selections}}}""");
        fixture.Store("Cape.One", """{"A":1,"B":2}""");

        // Act
        var exitCode = await fixture.Run();

        // Assert
        exitCode.Should().Be(0, fixture.Console.AllText);
        fixture.Written("Cape.One").Should().Be(expected);
    }
}
