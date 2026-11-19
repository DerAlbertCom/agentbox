using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Abox;
using Abox.Infrastructure;
using Abox.Naming;
using Abox.Sandboxes;
using NSubstitute;
using static Abox.Tests.PublishedSquadSourceFixture;

namespace Abox.Tests;

public sealed class PublishedSquadSourceTests
{
    public static bool IsNotWindows => !OperatingSystem.IsWindows();

    private readonly PublishedSquadSourceFixture _fixture = new();
    private IHostPrograms _programs => _fixture.Programs;
    private List<ProcessRequest> _calls => _fixture.Calls;
    private TestConsole _console => _fixture.Console;
    private JsonObject _metadata => _fixture.Metadata;

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task ReadsBundledMetadataAndPinsTheProcessedSetWithoutBuildingOrRunningAnything(
        bool index,
        bool gzip
    )
    {
        // Arrange
        _fixture.Index = index;
        _fixture.Gzip = gzip;

        // Act
        var result = await LoadAsync();

        // Assert
        result
            .Reference.Should()
            .Be("rwest.jfrog.io/release/agentbox-rwest-mti-ca/ac@" + PublishedSquadFactory.Digest);
        result.Metadata.State.Single().RelativePath.Should().Be(".published");
        result.Metadata.Mcp.Single().Url.Should().Be("https://example.com/mcp");
        result.Metadata.CreatedAt.Should().Be(DateTimeOffset.Parse("2026-11-09T12:00:00Z"));
        _calls.Should().OnlyContain(call => call.FileName == "docker" && !call.AttachConsole);
        _calls
            .Select(call => call.Arguments[0])
            .Should()
            .Equal(
                index
                    ? ["buildx", "buildx", "buildx", "buildx", "pull", "image"]
                    : ["buildx", "buildx", "buildx", "pull", "image"]
            );
        _calls
            .Single(call => call.Arguments[0] == "pull")
            .Arguments.Should()
            .Equal("pull", "--platform", "linux/" + ArchitectureName, Config);
        File.Exists(_calls.Single(call => call.Arguments[0] == "image").Arguments[3])
            .Should()
            .BeFalse("temporary archives are cleaned up");
    }

    [Fact]
    public async Task AcceptsTheOriginalPublishedMetadataFormatWithoutAnExplicitVersion()
    {
        // Arrange
        _metadata.Remove("SchemaVersion");

        // Act
        var result = await LoadAsync();

        // Assert
        result.Metadata.Squad.Should().Be("ac");
    }

    [Fact]
    public async Task RegistryIdentityIsCaseInsensitiveButTagsAreNot()
    {
        // Arrange
        _metadata["Team"] = "MTI-CA";
        _metadata["Squad"] = "AC";

        // Act
        var result = await LoadAsync();

        // Assert
        result.Metadata.Squad.Should().Be("ac");

        // Arrange
        _metadata["Tag"] = "LATEST";

        // Act
        var action = LoadAsync;

        // Assert
        await RejectAsync(action);
    }

    [Theory]
    [InlineData("Team", "other")]
    [InlineData("Squad", "other")]
    [InlineData("Tag", "other")]
    [InlineData("CreatedAt", "not-a-date")]
    public async Task RejectsWrongIdentityAndMalformedTimestamps(string field, string value)
    {
        // Arrange
        _metadata[field] = value;

        // Act
        var action = LoadAsync;

        // Assert
        await RejectAsync(action);
    }

    [Theory]
    [InlineData("State")]
    [InlineData("Mcp")]
    [InlineData("Team")]
    public async Task MissingRequiredFieldsCannotSilentlyDisableRuntimeConfiguration(string field)
    {
        // Arrange
        _metadata.Remove(field);

        // Act
        var action = LoadAsync;

        // Assert
        await RejectAsync(action);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("/absolute")]
    [InlineData("C:/outside")]
    [InlineData("..\\outside")]
    [InlineData("different")]
    public async Task RejectsUnsafeOrInconsistentHostStatePaths(string path)
    {
        // Arrange
        _metadata["State"]![0]!["RelativePath"] = path;

        // Act
        var action = LoadAsync;

        // Assert
        await RejectAsync(action);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("//")]
    [InlineData("/home/agent/../outside")]
    [InlineData("/home/agent/.published/")]
    [InlineData("/home/agent//.published")]
    public async Task RejectsNonCanonicalSandboxPaths(string path)
    {
        // Arrange
        _metadata["State"]![0]!["SandboxPath"] = path;

        // Act
        var action = LoadAsync;

        // Assert
        await RejectAsync(action);
    }

    [Theory]
    [InlineData("State")]
    [InlineData("Mcp")]
    public async Task RejectsDuplicateRuntimeEntries(string field)
    {
        // Arrange
        _metadata[field]!.AsArray().Add(_metadata[field]![0]!.DeepClone());

        // Act
        var action = LoadAsync;

        // Assert
        await RejectAsync(action);
    }

    [Theory]
    [InlineData("Url", "file:///local")]
    [InlineData("Url", "https://user:password@example.com/mcp")]
    [InlineData("Url", "not-a-url")]
    [InlineData("Url", "")]
    [InlineData("Name", "elastic,other")]
    [InlineData("Name", "-elastic")]
    [InlineData("Name", "élastic")]
    [InlineData("Name", "")]
    public async Task RejectsInvalidMcpDeclarations(string field, string value)
    {
        // Arrange
        _metadata["Mcp"]![0]![field] = value;

        // Act
        var action = LoadAsync;

        // Assert
        await RejectAsync(action);
    }

    [Theory]
    [InlineData("State")]
    [InlineData("Mcp")]
    public async Task RejectsNullEntries(string field)
    {
        // Arrange
        _metadata[field] = JsonNode.Parse("[null]");

        // Act
        var action = LoadAsync;

        // Assert
        await RejectAsync(action);
    }

    [Fact]
    public async Task RejectsUnsupportedMetadataSchema()
    {
        // Arrange
        _metadata["SchemaVersion"] = 2;

        // Act
        var action = LoadAsync;

        // Assert
        await RejectAsync(action);
    }

    [Fact]
    public async Task MissingBundledMetadataRequiresRepublishingInsteadOfLocalPreparation()
    {
        // Arrange
        _fixture.MetadataPath = null;

        // Act
        var action = LoadAsync;

        // Assert
        await RejectAsync(action);
    }

    [Fact]
    public async Task SymlinkMetadataIsNotFollowed()
    {
        // Arrange
        _fixture.MetadataPath = "symlink";

        // Act
        var action = LoadAsync;

        // Assert
        await RejectAsync(action);
    }

    [Fact]
    public async Task CorruptGzipIsReportedAsInvalidPublishedMetadata()
    {
        // Arrange
        _fixture.LayerBytes = [0x1f, 0x8b, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

        // Act
        var action = LoadAsync;

        // Assert
        await RejectAsync(action);
    }

    [Theory(
        SkipWhen = nameof(IsNotWindows),
        Skip = "Windows file sharing is needed to reproduce a locked download archive."
    )]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupFailureWarnsWithoutMaskingTheResultOrMetadataError(bool corrupt)
    {
        // Arrange
        _fixture.LockArchive = true;
        if (corrupt)
        {
            _fixture.LayerBytes = [0x1f, 0x8b, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        }

        try
        {
            if (corrupt)
            {
                // Act
                var action = LoadAsync;

                // Assert
                await RejectAsync(action);
            }
            else
            {
                // Act
                var result = await LoadAsync();

                // Assert
                result.Metadata.Squad.Should().Be("ac");
            }
            _console
                .Errors.Should()
                .Contain(error => error.Contains("warning:") && error.Contains("could not remove"));
        }
        finally
        {
            _fixture.LockedArchive?.Dispose();
            var archive = _calls.Single(call => call.Arguments[0] == "image").Arguments[3];
            Directory.Delete(Path.GetDirectoryName(archive)!, recursive: true);
        }
    }

    [Theory]
    [InlineData("config")]
    [InlineData("descriptor")]
    [InlineData("mount")]
    public async Task InvalidPublishedDescriptorsFailClosed(string error)
    {
        // Arrange
        _fixture.MissingConfig = error == "config";
        _fixture.WrongDescriptor = error == "descriptor";
        _fixture.MissingMount = error == "mount";

        // Act
        var action = LoadAsync;

        // Assert
        await RejectAsync(action);
    }

    [Theory]
    [InlineData("buildx")]
    [InlineData("pull")]
    [InlineData("image")]
    public async Task RegistryErrorsAreActionableAndDoNotEchoToolSecrets(string operation)
    {
        // Arrange
        _fixture.Failure = operation;

        // Act
        var action = LoadAsync;

        // Assert
        var error = (await action.Should().ThrowAsync<AboxException>()).Which;
        error
            .Message.Should()
            .Contain("read access")
            .And.Contain("not replaced")
            .And.NotContain("SECRET-NOT-FOR-OUTPUT");
    }

    [Fact]
    public async Task MissingDockerDoesNotRecommendALocalBuild()
    {
        // Arrange
        _programs.Exists("docker").Returns(false);

        // Act
        var action = LoadAsync;

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*Install Docker*no local build*"
        );
        _calls.Should().BeEmpty();
    }

    private Task<PublishedSquad> LoadAsync() => _fixture.LoadAsync();
}
