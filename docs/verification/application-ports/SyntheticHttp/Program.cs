using System.Net;
using System.Text;

if (args is ["--get"])
{
    using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });
    Console.WriteLine(await client.GetStringAsync("http://localhost:8203/"));
    return;
}

using var listener = new HttpListener();
listener.Prefixes.Add(args.Length == 0 ? "http://*:8203/" : args[0]);
listener.Start();
while (true)
{
    var context = await listener.GetContextAsync();
    var bytes = Encoding.UTF8.GetBytes("abox-issue43-dotnet-http-marker");
    context.Response.ContentType = "text/plain";
    context.Response.ContentLength64 = bytes.Length;
    await context.Response.OutputStream.WriteAsync(bytes);
    context.Response.Close();
}
