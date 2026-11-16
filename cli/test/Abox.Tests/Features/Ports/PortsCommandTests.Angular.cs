using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed partial class PortsCommandTests
{
    [Theory]
    [InlineData(
        "angular.json",
        "{\"projects\":{\"web\":{\"architect\":{\"serve\":{\"options\":{\"port\":4200}}}}}}"
    )]
    [InlineData("package.json", "{\"scripts\":{\"start\":\"ng serve --port 4200\"}}")]
    public async Task AngularFilesUseTheSharedArtifactAndDirectoryLinkDiscoveryBounds(
        string file,
        string json
    )
    {
        // Arrange
        using var fixture = new Fixture();
        using var links = new DirectoryLinks();
        foreach (
            var excluded in new[]
            {
                ".git",
                "bin",
                "obj",
                "node_modules",
                "dist",
                "coverage",
                ".angular",
                ".abox",
                ".vs",
            }
        )
            fixture.Root.File($"folder/{excluded}/{file}", "invalid excluded JSON");
        fixture.Root.File(file, "invalid parent JSON");
        fixture.Root.File("outside/" + file, "invalid escaped JSON");
        fixture.Root.File("folder/real/" + file, json);
        await links.Add(
            Path.Combine(fixture.Location, "escape"),
            Path.Combine(fixture.Root.Path, "outside")
        );
        await links.Add(Path.Combine(fixture.Location, "cycle"), fixture.Location);
        await links.Add(
            Path.Combine(fixture.Location, "alias"),
            Path.Combine(fixture.Location, "real")
        );

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(0);
        fixture.Console.Output.Count(line => line.Contains("| 4200 | 14200")).Should().Be(1);
    }

    [Theory]
    [InlineData("angular.json", false)]
    [InlineData("package.json", false)]
    [InlineData("angular.json", true)]
    [InlineData("package.json", true)]
    public async Task AngularFilesShareBoundedReadsAndMalformedEncodingRejection(
        string file,
        bool oversized
    )
    {
        // Arrange
        using var fixture = new Fixture();
        var path = fixture.Root.File("folder/" + file, "");
        await File.WriteAllBytesAsync(
            path,
            oversized ? new byte[1024 * 1024 + 1] : [0xff, 0xfe, 0xff]
        );

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(1);
        fixture
            .Console.AllText.Should()
            .Contain(file)
            .And.NotContain("Application ports published");
        await fixture.NoPublishing();
    }

    [Theory]
    [InlineData("angular.json")]
    [InlineData("package.json")]
    public async Task MatchingAngularFileLinksAreRejectedWithoutReadingTheirContents(string file)
    {
        // Arrange
        using var fixture = new Fixture();
        var target = fixture.Root.File("synthetic-target.json", "must-never-be-read");
        try
        {
            File.CreateSymbolicLink(Path.Combine(fixture.Location, file), target);
        }
        catch (Exception exception)
            when (exception is UnauthorizedAccessException or PlatformNotSupportedException
                || (
                    OperatingSystem.IsWindows()
                    && exception is IOException
                    && (exception.HResult & 0xffff) == 1314
                )
            )
        {
            throw new InvalidOperationException(
                Xunit.v3.DynamicSkipToken.Value
                    + "File symlinks require native link privileges on this host.",
                exception
            );
        }

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(1);
        fixture.Console.AllText.Should().Contain(file).And.NotContain("must-never-be-read");
        await fixture.NoPublishing();
    }

    [Theory]
    [InlineData("angular.json", "{}")]
    [InlineData(
        "angular.json",
        "{\"projects\":{\"web\":{\"architect\":{\"build\":{\"options\":{\"port\":4200}},\"serve\":{\"options\":{},\"configurations\":{\"dev\":{\"buildTarget\":\"web:build\"}}}}}}}"
    )]
    [InlineData("package.json", "{}")]
    [InlineData(
        "package.json",
        "{\"scripts\":{\"start\":\"ng serve\",\"start:dev\":\"ng serve --port 4200\"}}"
    )]
    [InlineData("package.json", "{\"scripts\":{\"start\":\"node server.js --portable\"}}")]
    [InlineData(
        "package.json",
        "{\"scripts\":{\"start\":\"cross-env PORT=4200 ng serve && other\"}}"
    )]
    public async Task AngularDiscoveryNeverInfersDefaultPortsOrScansOtherScriptsAndTargets(
        string file,
        string json
    )
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.Root.File("folder/" + file, json);

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(0);
        fixture.Console.AllText.Should().Contain("No explicit application ports");
        await fixture.NoPublishing();
    }

    [Fact]
    public async Task AllProjectsAndNamedServeConfigurationsCountWithoutResolvingBuildTargetsOrDefaults()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.Root.File(
            "folder/angular.json",
            "\ufeff"
                + """
                { // comments/trailing commas are supported by discovery
                  "projects":{
                    "one":{"architect":{"build":false,"serve":{
                      "defaultConfiguration":"not-selected",
                      "options":{"port":4200,"buildTarget":"not-evaluated"},
                      "configurations":{"dev":{"port":4201},"empty":{},"preview":{"port":4200}},
                    }}},
                    "two":{"targets":{"serve":{"options":{"port":55535}}}},
                  },
                }
                """
        );

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(0);
        fixture.Console.Output.Count(line => line.Contains(" | ")).Should().Be(5);
        fixture
            .Console.AllText.Should()
            .Contain("one.architect.serve.configurations.dev")
            .And.Contain("two.targets.serve.options")
            .And.Contain("| 55535 | 65535")
            .And.Contain("2 occurrences");
    }

    [Theory]
    [InlineData("angular.json", "{broken")]
    [InlineData("angular.json", "[]")]
    [InlineData("angular.json", "{\"projects\":[]}")]
    [InlineData("angular.json", "{\"projects\":{\"web\":null}}")]
    [InlineData("angular.json", "{\"projects\":{\"web\":{\"architect\":[]}}}")]
    [InlineData("angular.json", "{\"projects\":{\"web\":{\"targets\":{\"serve\":false}}}}")]
    [InlineData(
        "angular.json",
        "{\"projects\":{\"web\":{\"architect\":{\"serve\":{\"options\":[]}}}}}"
    )]
    [InlineData(
        "angular.json",
        "{\"projects\":{\"web\":{\"architect\":{\"serve\":{\"configurations\":[]}}}}}"
    )]
    [InlineData(
        "angular.json",
        "{\"projects\":{\"web\":{\"targets\":{\"serve\":{\"configurations\":{\"dev\":null}}}}}}"
    )]
    [InlineData(
        "angular.json",
        "{\"projects\":{\"web\":{\"architect\":{\"serve\":{\"options\":{\"port\":4200,\"port\":4201}}}}}}"
    )]
    [InlineData(
        "angular.json",
        "{\"projects\":{\"web\":{\"architect\":{\"serve\":{}},\"targets\":{\"serve\":{}}}}}"
    )]
    [InlineData("package.json", "{broken")]
    [InlineData("package.json", "[]")]
    [InlineData("package.json", "{\"scripts\":[]}")]
    [InlineData("package.json", "{\"scripts\":{\"start\":null}}")]
    [InlineData("package.json", "{\"scripts\":{\"start\":42}}")]
    [InlineData(
        "package.json",
        "{\"scripts\":{\"start\":\"ng serve\",\"start\":\"ng serve --port 4200\"}}"
    )]
    public async Task MalformedAngularInputsNameOnlyTheSourceAndPreventAllPublishing(
        string file,
        string json
    )
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.Root.File(
            "folder/a-api/Properties/launchSettings.json",
            """{"profiles":{"http":{"applicationUrl":"http://localhost:5000"}}}"""
        );
        fixture.Root.File("folder/z-web/" + file, json);

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(1);
        fixture
            .Console.AllText.Should()
            .Contain(file)
            .And.Contain("Fix")
            .And.NotContain("Application ports published");
        await fixture.NoPublishing();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("65536")]
    [InlineData("55536")]
    [InlineData("4200.5")]
    [InlineData("4.2e3")]
    [InlineData("\"4200\"")]
    [InlineData("\"${PORT}\"")]
    [InlineData("null")]
    [InlineData("true")]
    public async Task InvalidAngularConfigurationPortsAreRejectedBeforePublishing(string value)
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.Root.File(
            "folder/angular.json",
            "{\"projects\":{\"web\":{\"architect\":{\"serve\":{\"options\":{\"port\":"
                + value
                + "}}}}}}"
        );

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(1);
        fixture.Console.AllText.Should().Contain("angular.json").And.Contain("Fix");
        await fixture.NoPublishing();
    }

    [Theory]
    [InlineData("ng serve --port $PORT")]
    [InlineData("'ng' serve --port $PORT")]
    [InlineData("ng; other --port 4200")]
    [InlineData("ng serve --port %PORT%")]
    [InlineData("ng serve --port=${PORT:-4200}")]
    [InlineData("ng serve --port 4200 && echo do-not-echo-this-script")]
    [InlineData("ng serve --port 4200; echo do-not-echo-this-script")]
    [InlineData("ng serve --port 4200 | other")]
    [InlineData("ng serve --port 4200 > output")]
    [InlineData("ng serve --port 4200\nother")]
    [InlineData("ng serve --port 4200 # comment")]
    [InlineData("ng serve --port 4200 --host $HOST")]
    [InlineData("ng serve --port 4200 --configuration dev*")]
    [InlineData("ng serve -- --port 4200")]
    [InlineData("ng serve --port 4200 --port=4200")]
    [InlineData("ng serve --port")]
    [InlineData("ng serve --port:4200")]
    [InlineData("ng serve --port:4200 --port 4201")]
    [InlineData("ng serve --port=0")]
    [InlineData("ng serve --port=65536")]
    [InlineData("ng serve --port=55536")]
    [InlineData("ng serve --port=4.2e3")]
    [InlineData("ng serve --port='4200")]
    [InlineData("ng serve --port=42\\00")]
    [InlineData("ng run web:serve --port 4200")]
    public async Task DynamicAmbiguousOrUnsupportedStartPortsFailBeforeAnyMixedProjectPublishing(
        string script
    )
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.Root.File(
            "folder/a-api/Properties/launchSettings.json",
            """{"profiles":{"http":{"applicationUrl":"http://localhost:5000"}}}"""
        );
        fixture.Root.File(
            "folder/z-web/package.json",
            System.Text.Json.JsonSerializer.Serialize(new { scripts = new { start = script } })
        );

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(1);
        fixture
            .Console.AllText.Should()
            .Contain("package.json")
            .And.Contain("Fix")
            .And.NotContain("do-not-echo-this-script")
            .And.NotContain("Application ports published");
        await fixture.NoPublishing();
    }

    [Theory]
    [InlineData("vite --port 3000")]
    [InlineData("next dev --port 3000")]
    [InlineData("node custom.js --port 4200")]
    [InlineData("'vite' --port 3000")]
    [InlineData("vite --port '$PORT' && echo do-not-echo-this-script")]
    [InlineData("vite --port 'unclosed")]
    [InlineData("\"node custom.js --port 4200")]
    [InlineData("npx ng serve --port 4200")]
    [InlineData("cross-env PORT=4200 ng serve --port 4200")]
    [InlineData("npm run web -- --port 4200")]
    public async Task NonAngularStartScriptsDoNotBlockMixedDotnetPublishing(string script)
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.Root.File(
            "folder/api/Properties/launchSettings.json",
            """{"profiles":{"http":{"applicationUrl":"http://localhost:5000"}}}"""
        );
        fixture.Root.File(
            "folder/other/package.json",
            System.Text.Json.JsonSerializer.Serialize(new { scripts = new { start = script } })
        );

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(0);
        fixture
            .Console.AllText.Should()
            .Contain("| 5000 | 15000")
            .And.NotContain("do-not-echo-this-script");
        await fixture
            .Sbx.Received(1)
            .EnsureApplicationPortsAsync(
                fixture.Name,
                Arg.Is<IReadOnlyList<ApplicationPortMapping>>(mappings =>
                    mappings.Count == 1 && mappings[0] == new ApplicationPortMapping(5000, 15000)
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("ng serve --port 4203")]
    [InlineData("ng dev --port 4203")]
    [InlineData("ng dev web --port='4203'")]
    [InlineData("'ng' serve --port 4203")]
    [InlineData("\"ng\" s --port=4203")]
    [InlineData("ng serve --port=4203")]
    [InlineData("ng s web --configuration development --port 4203 --host localhost")]
    [InlineData("ng serve --port '4203'")]
    [InlineData("ng serve --port=\"4203\"")]
    [InlineData("ng serve \"--port=4203\"")]
    public async Task LiteralAngularStartScriptArgumentsAreDiscoveredWithoutExecutingTooling(
        string script
    )
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.Root.File(
            "folder/web/package.json",
            System.Text.Json.JsonSerializer.Serialize(
                new
                {
                    scripts = new
                    {
                        start = script,
                        prestart = "must never execute",
                        poststart = "must never execute",
                    },
                }
            )
        );

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(0);
        fixture
            .Console.AllText.Should()
            .Contain("package.json")
            .And.Contain("scripts.start")
            .And.Contain("| 4203 | 14203");
        await fixture
            .Sbx.Received(1)
            .EnsureApplicationPortsAsync(
                fixture.Name,
                Arg.Is<IReadOnlyList<ApplicationPortMapping>>(mappings =>
                    mappings.Count == 1 && mappings[0] == new ApplicationPortMapping(4203, 14203)
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task MixedProjectsReportEveryAngularSourceAndShareDuplicateMappingWithDotnet()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.Root.File(
            "folder/api/Properties/launchSettings.json",
            """{"profiles":{"http":{"applicationUrl":"http://localhost:4200"}}}"""
        );
        fixture.Root.File(
            "folder/web/angular.json",
            """
            {"projects":{"web":{"architect":{"serve":{
              "options":{"port":4200},
              "configurations":{"development":{"port":4201},"preview":{"port":4200}}
            }}}}}
            """
        );
        fixture.Root.File(
            "folder/web/package.json",
            """{"scripts":{"start":"ng serve --port 4202"}}"""
        );

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(0);
        fixture.Console.Output.Count(line => line.Contains(" | ")).Should().Be(6);
        fixture.Console.Output.Count(line => line.Contains("| 4200 | 14200")).Should().Be(3);
        fixture
            .Console.AllText.Should()
            .Contain("angular.json")
            .And.Contain("package.json")
            .And.Contain("launchSettings.json")
            .And.Contain("3 occurrences")
            .And.Contain("web.architect.serve.configurations.development");
        await fixture
            .Sbx.Received(1)
            .EnsureApplicationPortsAsync(
                fixture.Name,
                Arg.Is<IReadOnlyList<ApplicationPortMapping>>(mappings =>
                    mappings.Count == 3
                    && mappings.Contains(new ApplicationPortMapping(4200, 14200))
                    && mappings.Contains(new ApplicationPortMapping(4201, 14201))
                    && mappings.Contains(new ApplicationPortMapping(4202, 14202))
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
