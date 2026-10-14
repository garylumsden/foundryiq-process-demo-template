$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
. "$PSScriptRoot\environment.ps1"
& dotnet run --project tools\Workspace.Tools --no-restore -- evaluate
if ($LASTEXITCODE -ne 0) { throw 'Live acceptance failed. Inspect the retained evaluation results.' }
