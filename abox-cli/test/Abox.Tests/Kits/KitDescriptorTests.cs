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
    public void TheDomainsAnApiKeyIsSentToAreRead()
    {
        // Arrange
        _root.File(
            "kits/elastic-auth/elastic-auth.yaml",
            """
            schemaVersion: "3"
            kind: mixin
            capabilities:
              - type: com.docker.sandbox/credential@1
                optional: true
                config:
                  service: elastic
                  apiKey:
                    name: ELASTIC_API_KEY
                    inject:
                      - domain: api.elastic.co
                        header: Authorization
                      - domain: cloud.elastic.co
                        header: Authorization
            """
        );

        // Act
        var descriptor = KitDescriptor.Load(Path.Combine(_root.Path, "kits", "elastic-auth"));

        // Assert
        var service = descriptor!.Services.Should().ContainSingle().Which;
        service.Domains.Should().Equal("api.elastic.co", "cloud.elastic.co");
        service.Optional.Should().BeTrue();
    }

    [Fact]
    public void ACredentialWithoutAnApiKeyIsRequiredAndNamesNoDomains()
    {
        // Arrange
        _root.File(
            "kits/pi-config/pi-config.yaml",
            """
            schemaVersion: "3"
            kind: mixin
            capabilities:
              - type: com.docker.sandbox/credential@1
                config:
                  service: github
            """
        );

        // Act
        var descriptor = KitDescriptor.Load(Path.Combine(_root.Path, "kits", "pi-config"));

        // Assert
        var service = descriptor!.Services.Should().ContainSingle().Which;
        service.Domains.Should().BeEmpty();
        service.Optional.Should().BeFalse();
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
