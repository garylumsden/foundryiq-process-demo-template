$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
. "$PSScriptRoot\environment.ps1"
$approval = "create:$($env:AZURE_ENV_NAME):$($env:AZURE_SUBSCRIPTION_ID):$($env:AZURE_LOCATION)"
if ($env:WORKSPACE_DEPLOYMENT_APPROVAL -cne $approval) { throw 'Cloud configuration requires the exact process-only deployment approval.' }
foreach ($command in @('check-approval', 'ingest', 'manifests', 'deploy-agents')) {
    & dotnet run --project tools\Workspace.Tools --no-build -- $command
    if ($LASTEXITCODE -ne 0) { throw "Cloud configuration stopped at $command. Preserve the diagnostics." }
}
