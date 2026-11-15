using Abox.Infrastructure;
using Abox.Sandboxes;
using Abox.Sbx;

namespace Abox.Tests;

internal static class SandboxTest
{
    public static SandboxResolver Resolver(ISbxClient sbx) => new(sbx, new ProcessRunner());
}
