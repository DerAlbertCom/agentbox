using Abox.Kits;

namespace Abox.Tests;

public sealed class KitReferenceTests
{
    [Theory]
    [InlineData("elastic-auth", "elastic-auth")]
    [InlineData("docker.io/acme/pi-config", "docker.io/acme/pi-config")]
    [InlineData("docker.io/acme/pi-config:1.0", "docker.io/acme/pi-config")]
    [InlineData(
        "registry.example.com:5000/acme/pi-config:1.0",
        "registry.example.com:5000/acme/pi-config"
    )]
    public void TheRepositoryIsTheNameWithoutItsTag(string reference, string expected)
    {
        Assert.Equal(expected, KitReference.RepositoryOf(reference));
    }

    [Theory]
    [InlineData("elastic-auth", false)]
    [InlineData("docker.io/acme/pi-config", true)]
    [InlineData("docker.io/acme/pi-config:1.0", true)]
    public void AReferenceWithASlashOrAColonComesFromARegistry(string reference, bool expected)
    {
        Assert.Equal(expected, KitReference.IsRegistryReference(reference));
    }
}
