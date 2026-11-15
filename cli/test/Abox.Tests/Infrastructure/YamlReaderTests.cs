using Abox;
using Abox.Infrastructure;

namespace Abox.Tests;

public sealed class YamlReaderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScalarListErrorsRetainTheRootOrNestedKeyPath(bool nested)
    {
        // Arrange
        var reader = YamlReader.Parse(
            nested ? "section:\n  values: [one, {}]" : "values: [one, {}]",
            "source"
        );
        var key = nested ? "section.values" : "values";

        // Act
        var act = () =>
            nested
                ? reader.GetEntry("section")!.GetStringList("values")
                : reader.GetStringList("values");

        // Assert
        act.Should().Throw<AboxException>().WithMessage($"'source': '{key}' must be a value.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScalarMapErrorsRetainTheRootOrNestedKeyPath(bool nested)
    {
        // Arrange
        var reader = YamlReader.Parse(
            nested ? "section:\n  values: {item: []}" : "values: {item: []}",
            "source"
        );
        var key = nested ? "section.values.item" : "values.item";

        // Act
        var act = () =>
            nested
                ? reader.GetEntry("section")!.GetStringMap("values")
                : reader.GetStringMap("values");

        // Assert
        act.Should().Throw<AboxException>().WithMessage($"'source': '{key}' must be a value.");
    }

    [Fact]
    public void RootEntriesStillAcceptScalarsButNestedEntriesRequireMappings()
    {
        // Arrange
        var reader = YamlReader.Parse("scalar: value\nsection: {scalar: value}", "source");

        // Act
        var scalar = reader.GetEntry("scalar")!.ScalarValue;
        var act = () => reader.GetEntry("section")!.GetEntry("scalar");

        // Assert
        scalar.Should().Be("value");
        act.Should()
            .Throw<AboxException>()
            .WithMessage("'source': 'section.scalar' must be a mapping.");
    }
}
