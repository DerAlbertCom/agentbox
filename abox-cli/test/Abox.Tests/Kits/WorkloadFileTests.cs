using Abox.Kits;

namespace Abox.Tests;

public sealed class WorkloadFileTests
{
    private readonly TempDirectory _root = new();

    [Fact]
    public void ThePublishRepositoryIsReadFromTheWorkload()
    {
        // Arrange
        var path = _root.File(
            "sample.workload",
            "publishRepository: mtiae-developer-container-images-local\n"
        );

        // Act
        var workload = WorkloadFile.Load(path);

        // Assert
        workload.PublishRepository.Should().Be("mtiae-developer-container-images-local");
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"\"")]
    [InlineData("\" \"")]
    [InlineData("../other")]
    [InlineData("team/repository")]
    [InlineData("https://rwest.jfrog.io/repository")]
    [InlineData("repository:tag")]
    [InlineData("repository@sha256:abcd")]
    [InlineData("Repository")]
    [InlineData("-repository")]
    [InlineData("repository-")]
    [InlineData("\"repository\\n\"")]
    public void InvalidPublishRepositoriesAreRejected(string value)
    {
        // Arrange
        var path = _root.File("sample.workload", $"publishRepository: {value}\n");

        // Act
        var load = () => WorkloadFile.Load(path);

        // Assert
        load.Should().Throw<AboxException>().WithMessage("*publishRepository*not a valid*");
    }

    [Fact]
    public void LocalWorkloadsDoNotNeedAPublishRepository()
    {
        // Arrange
        var path = _root.File("sample.workload", "mise: []\n");

        // Act
        var workload = WorkloadFile.Load(path);

        // Assert
        workload.PublishRepository.Should().BeNull();
    }

    [Theory]
    [InlineData("[repository]")]
    [InlineData("{name: repository}")]
    public void ThePublishRepositoryMustBeAScalar(string value)
    {
        // Arrange
        var path = _root.File("sample.workload", $"publishRepository: {value}\n");

        // Act
        var load = () => WorkloadFile.Load(path);

        // Assert
        load.Should().Throw<AboxException>().WithMessage("*publishRepository*must be a value*");
    }
}
