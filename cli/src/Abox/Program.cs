using Abox.Cli;
using Abox.Infrastructure;

var console = new SystemConsole();
var services = new AboxServices(console);
var root = AboxCommand.Create(services);

return await root.Parse(args).InvokeAsync();
