using Abox.Kits;

namespace Abox.Tests;

public sealed class KitDescriptorTests
{
    private readonly TempDirectory _root = new();

    [Fact]
    public void TheServicesAKitAsksForAreRead()
    {
        // Arrange
        _root.File(
            "kits/pi-config/pi-config.yaml",
            """
            # syntax=docker/sandbox-kit:3
            schemaVersion: "3"
            kind: mixin
            capabilities:
              - type: com.docker.sandbox/credential@1
                config:
                  service: github
                  phase: runtime
                  apiKey:
                    name: GITHUB_TOKEN
                    proxyManaged: true
                    inject:
                      - domain: api.github.com
                        header: Authorization
                        format: "Bearer %s"
            """
        );

        // Act
        var descriptor = KitDescriptor.Load(Path.Combine(_root.Path, "kits", "pi-config"));

        // Assert
        descriptor.Should().NotBeNull();
        var service = descriptor!.Services.Should().ContainSingle().Which;
        service.Service.Should().Be("github");
    }

    [Fact]
    public void ACapabilityThatIsNotACredentialIsIgnored()
    {
        // Arrange
        _root.File(
            "kits/pi-config/pi-config.yaml",
            """
            schemaVersion: "3"
            kind: mixin
            capabilities:
              - type: com.docker.sandbox/network-policy@1
                config:
                  runtime:
                    allow:
                      - api.github.com
            """
        );

        // Act
        var descriptor = KitDescriptor.Load(Path.Combine(_root.Path, "kits", "pi-config"));

        // Assert
        descriptor.Should().NotBeNull();
        descriptor!.Services.Should().BeEmpty();
    }

    [Fact]
    public void AKitWithoutADescriptorHasNoServices()
    {
        // Arrange
        _root.CreateDirectory("kits/empty");

        // Act
        var descriptor = KitDescriptor.Load(Path.Combine(_root.Path, "kits", "empty"));

        // Assert
        descriptor.Should().BeNull();
    }
}
