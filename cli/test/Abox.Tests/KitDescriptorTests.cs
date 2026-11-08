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

        Assert.NotNull(descriptor);
        var service = Assert.Single(descriptor!.Services);
        Assert.Equal("github", service.Service);
        Assert.Equal(["runtime"], service.Phases);
        Assert.Equal("GITHUB_TOKEN", service.EnvironmentVariable);
        Assert.Equal(["api.github.com"], service.Domains);
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

        Assert.NotNull(descriptor);
        Assert.Equal(["build", "runtime"], Assert.Single(descriptor!.Services).Phases);
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

        Assert.NotNull(descriptor);
        Assert.Empty(descriptor!.Services);
    }

    [Fact]
    public void AKitWithoutADescriptorHasNoServices()
    {
        _root.Directory_("kits/empty");

        Assert.Null(KitDescriptor.Load(Path.Combine(_root.Path, "kits", "empty")));
    }
}
