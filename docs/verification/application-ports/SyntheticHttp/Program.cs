using System.Net;
using System.Text;

if (args is ["--get"] or ["--get", _])
{
    var port =
        args.Length == 2
            ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture)
            : 8203;
    using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });
    Console.WriteLine(await client.GetStringAsync($"http://localhost:{port}/"));
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
