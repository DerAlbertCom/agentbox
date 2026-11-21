#!/usr/bin/env pwsh
$project = Join-Path $PSScriptRoot 'abox-cli/src/Abox/Abox.csproj'
dotnet run --project $project -- @args
exit $LASTEXITCODE
