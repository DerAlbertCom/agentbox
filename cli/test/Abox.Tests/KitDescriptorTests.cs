using Abox.Kits;

namespace Abox.Tests;

public sealed class KitDescriptorTests
{
    private readonly TempDirectory _root = new();

    [Fact]
    public void TheServicesAKitAsksForAreRead()
    {
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

        var descriptor = KitDescriptor.Load(Path.Combine(_root.Path, "kits", "pi-config"));

        descriptor.Should().NotBeNull();
        var service = descriptor!.Services.Should().ContainSingle().Which;
        service.Service.Should().Be("github");
        service.Phases.Should().Equal("runtime");
        service.EnvironmentVariable.Should().Be("GITHUB_TOKEN");
        service.Domains.Should().Equal("api.github.com");
    }

    [Fact]
    public void AServiceWithSeveralPhasesIsRead()
    {
        _root.File(
            "kits/pi-config/pi-config.yaml",
            """
            schemaVersion: "3"
            kind: mixin
            capabilities:
              - type: com.docker.sandbox/credential@1
                config:
                  service: github
                  phase:
                    - build
                    - runtime
            """
        );

        var descriptor = KitDescriptor.Load(Path.Combine(_root.Path, "kits", "pi-config"));

        descriptor.Should().NotBeNull();
        descriptor!
            .Services.Should()
            .ContainSingle()
            .Which.Phases.Should()
            .Equal("build", "runtime");
    }

    [Fact]
    public void ACapabilityThatIsNotACredentialIsIgnored()
    {
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

        var descriptor = KitDescriptor.Load(Path.Combine(_root.Path, "kits", "pi-config"));

        descriptor.Should().NotBeNull();
        descriptor!.Services.Should().BeEmpty();
    }

    [Fact]
    public void AKitWithoutADescriptorHasNoServices()
    {
        _root.Directory_("kits/empty");

        KitDescriptor.Load(Path.Combine(_root.Path, "kits", "empty")).Should().BeNull();
    }
}
