using System.Text.Json;

namespace Abox.ApplicationPorts;

internal static class AngularConfiguration
{
    internal static void AddPorts(JsonElement root, string source, List<AppPortOccurrence> result)
    {
        if (!root.TryGetProperty("projects", out var projects))
            return;
        foreach (var project in projects.EnumerateObject())
        {
            var foundServe = false;
            foreach (var container in new[] { "architect", "targets" })
            {
                if (!project.Value.TryGetProperty(container, out var targets))
                    continue;
                if (!targets.TryGetProperty("serve", out var serve))
                    continue;
                if (foundServe)
                    throw new FormatException();
                foundServe = true;
                var context = $"{project.Name}.{container}.serve";
                if (serve.TryGetProperty("options", out var options))
                    AddPort(options, source, context + ".options", result);
                if (!serve.TryGetProperty("configurations", out var configurations))
                    continue;
                foreach (var configuration in configurations.EnumerateObject())
                    AddPort(
                        configuration.Value,
                        source,
                        context + ".configurations." + configuration.Name,
                        result
                    );
            }
        }
    }

    private static void AddPort(
        JsonElement options,
        string source,
        string context,
        List<AppPortOccurrence> result
    )
    {
        if (!options.TryGetProperty("port", out var value))
            return;
        if (!value.TryGetInt32(out var port) || port is < 1 or > 65535)
            throw new FormatException();
        result.Add(new AppPortOccurrence(source, context, port));
    }
}
