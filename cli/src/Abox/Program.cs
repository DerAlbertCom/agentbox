using Abox.Cli;
using Abox.Infrastructure;

var console = new SystemConsole();
var services = new AboxServices(console);

var (arguments, trailing) = CommandLine.Split(args);
services.Trailing.Values = trailing;

var root = AboxCommand.Create(services);

return await root.Parse(arguments).InvokeAsync();
