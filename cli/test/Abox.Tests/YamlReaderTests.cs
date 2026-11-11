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
        var reader = YamlReader.Parse(
            nested ? "section:\n  values: [one, {}]" : "values: [one, {}]",
            "source"
        );
        var key = nested ? "section.values" : "values";

        var act = () =>
            nested
                ? reader.GetEntry("section")!.GetStringList("values")
                : reader.GetStringList("values");

        act.Should().Throw<AboxException>().WithMessage($"'source': '{key}' must be a value.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScalarMapErrorsRetainTheRootOrNestedKeyPath(bool nested)
    {
        var reader = YamlReader.Parse(
            nested ? "section:\n  values: {item: []}" : "values: {item: []}",
            "source"
        );
        var key = nested ? "section.values.item" : "values.item";

        var act = () =>
            nested
                ? reader.GetEntry("section")!.GetStringMap("values")
                : reader.GetStringMap("values");

        act.Should().Throw<AboxException>().WithMessage($"'source': '{key}' must be a value.");
    }

    [Fact]
    public void RootEntriesStillAcceptScalarsButNestedEntriesRequireMappings()
    {
        var reader = YamlReader.Parse("scalar: value\nsection: {scalar: value}", "source");

        reader.GetEntry("scalar")!.ScalarValue.Should().Be("value");
        var act = () => reader.GetEntry("section")!.GetEntry("scalar");

        act.Should()
            .Throw<AboxException>()
            .WithMessage("'source': 'section.scalar' must be a mapping.");
    }
}
