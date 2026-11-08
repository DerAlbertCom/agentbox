using Abox;
using Abox.Cli;
using Abox.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

var console = new SystemConsole();
var (arguments, trailing) = CommandLine.Split(args);

var services = new ServiceCollection();
services.AddAbox(console, new TrailingArguments { Values = trailing });

try
{
    using var provider = services.BuildServiceProvider();
    return await AboxCommand.Create(provider).Parse(arguments).InvokeAsync();
}
catch (AboxException exception)
{
    console.WriteError($"error: {exception.Message}");
    return 1;
}
