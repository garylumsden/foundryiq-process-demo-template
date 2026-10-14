param([switch]$Cloud)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
if ($Cloud) { . "$PSScriptRoot\environment.ps1" }
Push-Location src\Workspace.Web\frontend
try {
    & npm run build
    if ($LASTEXITCODE -ne 0) { throw 'Restore the frontend dependencies before starting the app.' }
} finally { Pop-Location }
& dotnet run --project src\Workspace.Web --no-restore
if ($LASTEXITCODE -ne 0) { throw 'The application stopped with an error.' }
