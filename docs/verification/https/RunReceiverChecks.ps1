param([string]$Image = 'cao-docker-ai-sandbox:latest', [string]$Test)
$ErrorActionPreference = 'Stop'
# No host mounts, network, certificate exports or package installations. Only a
# newly created --rm container's writable layer receives these public sources.
$root = (Resolve-Path "$PSScriptRoot/../../..").Path
$files = @{}
@('cli/src/Abox/Sbx/development_certificate.py',
  'cli/src/Abox/Sbx/development_certificate_trust.py',
  'cli/src/Abox/Sbx/development_certificate_exec.sh',
  'cli/src/Abox/Sbx/SbxClient.HttpsExec.cs',
  'docs/verification/https/ReceiverChecks.py') | ForEach-Object {
    if (Test-Path "$root/$_") { $files[(Split-Path $_ -Leaf)] = [IO.File]::ReadAllText("$root/$_") }
  }
$loader = 'import json,os,sys; files=json.load(sys.stdin); os.mkdir("/opt/abox-receiver-checks"); os.chdir("/opt/abox-receiver-checks"); [open(k,"w").write(v) for k,v in files.items()]; os.execvp("python3",["python3","ReceiverChecks.py"]+sys.argv[1:])'
$testArguments = @()
if ($Test) { $testArguments = @($Test) }
$files | ConvertTo-Json -Compress | docker run --rm --pull never --network none --user 0 --env ABOX_DISPOSABLE_RECEIVER_CHECKS=1 --entrypoint python3 -i $Image -c $loader @testArguments
if ($LASTEXITCODE -ne 0) { throw "Disposable receiver checks failed (exit $LASTEXITCODE)." }
$files | ConvertTo-Json -Compress | docker run --rm --pull never --network none --user 0 --env ABOX_DISPOSABLE_RECEIVER_CHECKS=1 `
  --tmpfs /tmp:mode=1777 --tmpfs /usr/local/share/ca-certificates/abox-development-https-1000:mode=755 `
  --entrypoint python3 -i $Image -c $loader --mount-rejection-only
if ($LASTEXITCODE -ne 0) { throw "Disposable mount rejection checks failed (exit $LASTEXITCODE)." }
