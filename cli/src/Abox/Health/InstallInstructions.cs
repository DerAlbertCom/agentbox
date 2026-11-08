namespace Abox.Health;

/// <summary>
/// How to install sbx on the current system, as documented at
/// https://docs.docker.com/ai/sandboxes/install/.
/// </summary>
public static class InstallInstructions
{
    public static IReadOnlyList<string> ForCurrentSystem()
    {
        if (OperatingSystem.IsMacOS())
        {
            return
            [
                "Install sbx with Homebrew:",
                "  brew trust docker/tap",
                "  brew install docker/tap/sbx",
            ];
        }

        if (OperatingSystem.IsWindows())
        {
            return
            [
                "Install sbx on Windows 11:",
                "  winget install -h Docker.sbx",
                "For all users, from an elevated prompt:",
                "  msiexec.exe /i DockerSandboxesMachine.msi /quiet",
            ];
        }

        if (OperatingSystem.IsLinux())
        {
            return
            [
                "Install sbx on Ubuntu 24.04 or later:",
                "  curl -fsSL https://get.docker.com | sudo SBX=1 sh",
                "For sbx only:",
                "  curl -fsSL https://get.docker.com | sudo REPO_ONLY=1 sh",
                "  sudo apt install docker-sbx",
                "Local sandboxes need KVM:",
                "  sudo usermod -aG kvm $USER",
            ];
        }

        return
        [
            "Install sbx from the release artifacts:",
            "  https://docs.docker.com/ai/sandboxes/install/",
        ];
    }
}
