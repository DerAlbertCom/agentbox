using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.UserSecrets;

const string id = "abox-issue5-synthetic-only";
Console.WriteLine($"runtime={Environment.Version}");
Console.WriteLine($"home={Environment.GetEnvironmentVariable("HOME")}");
Console.WriteLine($"appdata={Environment.GetEnvironmentVariable("APPDATA")}");
Console.WriteLine($"destination={PathHelper.GetSecretsPathFromSecretsId(id)}");

var configuration = new ConfigurationBuilder().AddUserSecrets(id).Build();
if (
    configuration["Probe:Marker"] != "synthetic-only-å-雪"
    || configuration["Probe:Line"] != "one\ntwo"
)
{
    Console.Error.WriteLine("Synthetic payload not available or altered.");
    return 42;
}

Console.WriteLine("User-secrets read verified; values not printed.");
return 0;
