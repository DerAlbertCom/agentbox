using Abox;
using Abox.Cli;
using Abox.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var console = new SystemConsole();
var (arguments, trailing) = CommandLine.Split(args);

var builder = Host.CreateApplicationBuilder();
builder.Services.AddAbox(console, new TrailingArguments { Values = trailing });

try
{
    using var host = builder.Build();
    return await host.Services.GetRequiredService<AboxRootCommand>().Parse(arguments).InvokeAsync();
}
catch (AboxException exception)
{
    console.WriteError($"error: {exception.Message}");
    return 1;
}
