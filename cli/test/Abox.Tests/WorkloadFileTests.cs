using Abox.Kits;

namespace Abox.Tests;

public sealed class WorkloadFileTests
{
    private readonly TempDirectory _root = new();

    [Fact]
    public void ThePublishRepositoryIsReadFromTheWorkload()
    {
        var path = _root.File(
            "sample.workload",
            "publishRepository: mtiae-developer-container-images-local\n"
        );

        var workload = WorkloadFile.Load(path);

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
        var path = _root.File("sample.workload", $"publishRepository: {value}\n");

        var load = () => WorkloadFile.Load(path);

        load.Should().Throw<AboxException>().WithMessage("*publishRepository*not a valid*");
    }

    [Fact]
    public void LocalWorkloadsDoNotNeedAPublishRepository()
    {
        var path = _root.File("sample.workload", "mise: []\n");

        WorkloadFile.Load(path).PublishRepository.Should().BeNull();
    }

    [Theory]
    [InlineData("[repository]")]
    [InlineData("{name: repository}")]
    public void ThePublishRepositoryMustBeAScalar(string value)
    {
        var path = _root.File("sample.workload", $"publishRepository: {value}\n");

        var load = () => WorkloadFile.Load(path);

        load.Should().Throw<AboxException>().WithMessage("*publishRepository*must be a value*");
    }
}
