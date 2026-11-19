# CurrentUser CurrentHost profile for pwsh, baked into ~/.config/powershell (see
# template/Dockerfile.in), not the versioned $PSHOME, so mise upgrades retain it.
# The CurrentHost filename leaves all-hosts profile.ps1 free; stacked kits may
# override this image default with files/home/.config/powershell/Microsoft.PowerShell_profile.ps1.
#
# Every block is guarded: a missing binary or module degrades the shell instead of breaking it.

# Environment restoration is safe without a console and lets SSH/Herdr commands
# and generated apphosts work even when Docker's environment was not inherited.
if (-not $env:DOTNET_DOTNETUP_DATA_DIR) { $env:DOTNET_DOTNETUP_DATA_DIR = "$HOME/.local/share/dotnetup" }
if (-not $env:DOTNET_ROOT) { $env:DOTNET_ROOT = "$env:DOTNET_DOTNETUP_DATA_DIR/dotnet" }
if (-not $env:DOTNET_NOLOGO) { $env:DOTNET_NOLOGO = '1' }
if (-not $env:DOTNET_CLI_TELEMETRY_OPTOUT) { $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1' }
foreach ($dotnetPath in @($env:DOTNET_ROOT, "$HOME/.dotnetup")) {
    if (($env:PATH -split [IO.Path]::PathSeparator) -notcontains $dotnetPath) {
        $env:PATH = $dotnetPath + [IO.Path]::PathSeparator + $env:PATH
    }
}

# Terminal UX is not useful without a console, and two parts are actively harmful without one:
# Set-PSReadLineOption -PredictionViewStyle ListView warns that the window is too small, and an
# imported CompletionPredictor keeps a foreground thread alive that stops pwsh exiting at all. Kit
# setup commands and agents shelling out to `pwsh -Command ...` get neither. Gate on a real console
# rather than on $Host alone, which is still ConsoleHost when stdio is piped.
$sandboxInteractiveConsole = $Host.Name -eq 'ConsoleHost' -and
    -not [Console]::IsInputRedirected -and -not [Console]::IsOutputRedirected

if ($sandboxInteractiveConsole) {

    # PSReadLine first and independent of everything below, so predictions, editing keys and history
    # still apply even if the prompt or completions fail to initialise. HistoryAndPlugin lets plugin
    # predictors (CompletionPredictor, imported next) surface alongside history in the ListView.
    # SaveIncrementally is what lets several herdr tabs in one sandbox share a history file.
    try {
        Set-PSReadLineOption -PredictionSource HistoryAndPlugin
        Set-PSReadLineOption -PredictionViewStyle ListView
        Set-PSReadLineOption -EditMode Windows
        Set-PSReadLineOption -HistorySaveStyle SaveIncrementally
    }
    catch {
        Write-Warning "PSReadLine configuration skipped: $($_.Exception.Message)"
    }

    # A predictor is only active once its module is imported; installing it (Dockerfile) is not
    # enough. It surfaces registered argument completers as ListView predictions as you type rather
    # than only on Tab.
    #
    # Deliberately nothing else touches completion here: this profile mirrors the Carmen.Shared
    # build-container pwsh experience (PSReadLine + CompletionPredictor + oh-my-posh), so Tab keeps
    # the EditMode Windows default (TabCompleteNext) and no extra completion engine is wired in.
    # carapace is a BASH-only tool in this image (see ~/.bashrc); do not init it for pwsh.
    try {
        Import-Module CompletionPredictor -ErrorAction Stop
    }
    catch {
        Write-Warning "CompletionPredictor not loaded: $($_.Exception.Message)"
    }

    # oh-my-posh drives the prompt. Bash uses starship (see ~/.bashrc); the two shells deliberately
    # do not share a prompt config. The theme is the home-relative file, which stacked kits may
    # override by shipping files/home/.config/oh-my-posh/theme.json; the copy under /opt/sandbox is
    # the immutable fallback so there is always a prompt.
    try {
        . /opt/sandbox/pwsh/_OmpTheme.ps1
        $sandboxTheme = Resolve-SandboxOmpTheme -HomePath $HOME `
            -RelativePath '.config/oh-my-posh/theme.json' `
            -BakedDefaultPath '/opt/sandbox/pwsh/theme.json'
        if ($sandboxTheme.Warning) { Write-Warning $sandboxTheme.Warning }
        if (Get-Command oh-my-posh -ErrorAction SilentlyContinue) {
            oh-my-posh init pwsh --config $sandboxTheme.ConfigPath | Invoke-Expression
        }
    }
    catch {
        Write-Warning "oh-my-posh prompt not initialised: $($_.Exception.Message)"
    }
}
