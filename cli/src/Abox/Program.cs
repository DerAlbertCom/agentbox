using Abox.Cli;
using Abox.Features;
using Abox.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

var console = new SystemConsole();

return await CommandHandling.RunAsync(
    async () =>
    {
        var (arguments, trailing) = CommandLine.Split(args);
        var services = new ServiceCollection().AddAbox(
            console,
            new TrailingArguments { Values = trailing }
        );
        using var provider = services.BuildServiceProvider();
        return await provider.GetRequiredService<AboxRootCommand>().InvokeAsync(arguments);
    },
    console
);
