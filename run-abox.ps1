$project = Join-Path $PSScriptRoot 'cli/src/Abox/Abox.csproj'
dotnet run --project $project -- --verbose @args
exit $LASTEXITCODE
