$ErrorActionPreference = 'Stop'
$values = & azd env get-values --output json
if ($LASTEXITCODE -ne 0) { throw 'Select an AZD environment in this repository before using cloud commands.' }
$values = $values | ConvertFrom-Json
if (@($values.PSObject.Properties.Name | Where-Object { $_ -in @('WORKSPACE_DEPLOYMENT_APPROVAL', 'WORKSPACE_DELETE_APPROVAL') }).Count -gt 0) {
    throw 'Remove persisted deployment or deletion approval from the AZD environment. Supply approval only in the current process.'
}
$values.PSObject.Properties | ForEach-Object {
    [Environment]::SetEnvironmentVariable($_.Name, [string]$_.Value, 'Process')
}
$account = & az account show --output json
if ($LASTEXITCODE -ne 0) { throw 'Azure CLI authentication is unavailable. Sign in separately.' }
$account = $account | ConvertFrom-Json
if ($account.id -ne $env:AZURE_SUBSCRIPTION_ID) { throw 'The Azure CLI subscription does not match this AZD environment.' }
