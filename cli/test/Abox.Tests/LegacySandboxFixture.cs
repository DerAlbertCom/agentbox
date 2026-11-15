namespace Abox.Tests;

internal static class LegacySandboxFixture
{
    // Independent literals from cape-agent-sandbox 1512ff7, not production naming helpers.
    public static string Location =>
        OperatingSystem.IsWindows()
            ? @"C:\__agentbox_legacy_fixtures__\Case_Name"
            : "/__agentbox_legacy_fixtures__/Case_Name";

    public static string Name =>
        OperatingSystem.IsWindows() ? "case_name-ai-sbx-cca0eb" : "case_name-ai-sbx-e479dd";
}
