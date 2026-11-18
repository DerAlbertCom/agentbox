#Requires -Version 7.5

# Prefer the editable home theme; fall back to the baked default.

function Resolve-SandboxOmpTheme {
    param(
        [Parameter(Mandatory)] [string] $HomePath,
        [Parameter(Mandatory)] [string] $RelativePath,
        [Parameter(Mandatory)] [string] $BakedDefaultPath,
        [scriptblock] $TestPath = { param($Path) Test-Path -LiteralPath $Path -PathType Leaf }
    )

    # These are guest paths, even during host-side checks.
    $homeTheme = ($HomePath.TrimEnd('/') + '/' + $RelativePath.TrimStart('/'))

    if (& $TestPath $homeTheme) {
        return @{ ConfigPath = $homeTheme; Warning = $null }
    }

    return @{
        ConfigPath = $BakedDefaultPath
        Warning    = "No oh-my-posh theme at '$homeTheme' - using the image default '$BakedDefaultPath'."
    }
}
