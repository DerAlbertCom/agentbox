param(
    [Parameter(Mandatory = $true)][string]$ScratchRoot,
    [string]$BaselineRevision = '7e37db305f96b75494802e30b9dfc4aa42a260d8',
    [string]$CandidateRevision = 'HEAD'
)

$ErrorActionPreference = 'Stop'
$repo = (git -C $PSScriptRoot rev-parse --show-toplevel).Trim()
if ($LASTEXITCODE) { throw 'Cannot find repository.' }
$baseline = (git -C $repo rev-parse $BaselineRevision).Trim()
if ($LASTEXITCODE) { throw 'Cannot resolve baseline revision.' }
$candidate = (git -C $repo rev-parse $CandidateRevision).Trim()
if ($LASTEXITCODE) { throw 'Cannot resolve candidate revision.' }
$scratch = Join-Path ([IO.Path]::GetFullPath($ScratchRoot)) ('discovery38-' + [guid]::NewGuid().ToString('N'))
if (Test-Path $scratch) { throw 'Scratch directory must not already exist.' }
New-Item -ItemType Directory $scratch | Out-Null

# Export committed sources, not another working tree or local investigation artifacts.
foreach ($version in @(@('baseline', $baseline), @('candidate', $candidate))) {
    $name, $revision = $version
    $archive = Join-Path $scratch "$name.zip"
    git -C $repo archive --format=zip "--output=$archive" $revision cli/src README.md global.json
    if ($LASTEXITCODE) { throw "Cannot archive $name." }
    $source = Join-Path $scratch $name
    Expand-Archive $archive $source
    dotnet build (Join-Path $source 'cli/src/Abox/Abox.csproj') -c Release -o (Join-Path $source 'built')
    if ($LASTEXITCODE) { throw "Cannot build $name." }
}
$probe = Join-Path $scratch 'probe'
dotnet build (Join-Path $PSScriptRoot 'Discovery38Probe.csproj') -c Release -o $probe
if ($LASTEXITCODE) { throw 'Cannot build timing probe.' }
$result = Join-Path $scratch 'results.json'
dotnet (Join-Path $probe 'Discovery38Probe.dll') `
    (Join-Path $scratch 'baseline/built/abox.dll') `
    (Join-Path $scratch 'candidate/built/abox.dll') `
    (Join-Path $scratch 'synthetic-snapshot') | Set-Content -Encoding utf8 $result
if ($LASTEXITCODE) { throw 'Discovery comparison failed.' }
@{ baseline = $baseline; candidate = $candidate; sdk = (dotnet --version) } |
    ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $scratch 'revisions.json')
Get-Content $result
Write-Host "Results and archived-build provenance: $scratch"
# Keep results/builds for inspection; the probe deletes its synthetic snapshot in finally.
