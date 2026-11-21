using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class AppPortsLifecycleTransportTests
{
    [Fact]
    public async Task LocalCreateAndOnDemandRetryUseTheSameRelayAdapterButOrdinaryRunDoesNot()
    {
        // Arrange
        using var fixture = new AppPortsLifecycleFixture();
        fixture.Angular();
        var transport = new Transport(fixture);
        fixture.Services.AddSingleton<ISbxClient>(
            new SbxClient(transport.Runner, AboxPaths.For(fixture.Root.Path, fixture.Root.Path))
        );

        // Act
        var created = await fixture.Create(true);
        var reused = await fixture.Invoke("run", "shell");
        var retried = await fixture.Invoke("ports", "--location", fixture.Location);

        // Assert
        created.Should().Be(0, fixture.Console.AllText);
        reused.Should().Be(0, fixture.Console.AllText);
        retried.Should().Be(0, fixture.Console.AllText);
        var relays = transport
            .Calls.Where(request => request.Arguments.Contains("python3"))
            .ToArray();
        relays.Should().HaveCount(2, "ordinary run does not rediscover or provision ports");
        relays[1]
            .Arguments.Should()
            .ContainInOrder(
                "--reserved",
                "60044",
                "--offset",
                "10000",
                "--bindings",
                "[[60044,14200]]"
            );
        transport.Calls.Should().ContainSingle(request => request.Arguments.Contains("--publish"));
        transport.Calls.Should().NotContain(request => request.Arguments.Contains("--unpublish"));
    }

    private sealed class Transport
    {
        public IProcessRunner Runner { get; } = Substitute.For<IProcessRunner>();
        public List<ProcessRequest> Calls { get; } = [];
        private readonly AppPortsLifecycleFixture fixture;
        private bool created;
        private bool published;

        public Transport(AppPortsLifecycleFixture fixture)
        {
            this.fixture = fixture;
            Runner
                .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
                .Returns(call => Run(call.Arg<ProcessRequest>()));
        }

        private ProcessResult Run(ProcessRequest request)
        {
            if (request.FileName != "sbx")
                return LocalWorkloadFactory.Result(request);
            Calls.Add(request);
            var args = request.Arguments;
            var output = args[0] switch
            {
                "version" => """{"client":{"version":"0.51.0"}}""",
                "diagnose" => """{"checks":[]}""",
                "create" => Created(),
                "ls" => created ? $$"""[{"name":"{{fixture.Name}}","status":"stopped"}]""" : "[]",
                "exec" when args.Contains("python3") =>
                    """[{"application_port":4200,"relay_port":60044}]""",
                "exec" => "",
                "ports" when args.Contains("--publish") => Published(),
                "ports" => published
                    ? """[{"host_ip":"127.0.0.1","host_port":14200,"sandbox_port":60044,"protocol":"tcp4"}]"""
                    : "[]",
                _ => throw new InvalidOperationException("Unexpected synthetic sbx operation"),
            };
            return new ProcessResult(0, output, "");
        }

        private string Created()
        {
            created = true;
            return "";
        }

        private string Published()
        {
            fixture
                .Records.Load(fixture.Name)
                .Should()
                .NotBeNull("runtime publication follows record save");
            published = true;
            return "[]";
        }
    }
}
