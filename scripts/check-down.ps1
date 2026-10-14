$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
. "$PSScriptRoot\environment.ps1"
$approval = "delete:$($env:AZURE_ENV_NAME):$($env:AZURE_SUBSCRIPTION_ID)"
if ($env:WORKSPACE_DELETE_APPROVAL -cne $approval) {
    throw 'Deletion requires process-only WORKSPACE_DELETE_APPROVAL=delete:<environment>:<subscription>. Confirm this exact environment before continuing.'
}
