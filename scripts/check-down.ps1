$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
. "$PSScriptRoot\environment.ps1"
$approval = "delete:$($env:AZURE_ENV_NAME):$($env:AZURE_SUBSCRIPTION_ID)"
if ($env:WORKSPACE_DELETE_APPROVAL -cne $approval) {
    throw 'Deletion requires process-only WORKSPACE_DELETE_APPROVAL=delete:<environment>:<subscription>. Confirm this exact environment before continuing.'
}
$expectedGroup = "rg-$($values.AZURE_ENV_NAME)"
if ($values.AZURE_RESOURCE_GROUP -ine $expectedGroup) {
    throw 'Refusing teardown: the selected resource group does not match this environment.'
}
$group = & az group show --name $expectedGroup --subscription $values.AZURE_SUBSCRIPTION_ID --output json
if ($LASTEXITCODE -ne 0) { throw 'Cannot verify the resource-group ownership for teardown.' }
$group = $group | ConvertFrom-Json
$expectedId = "/subscriptions/$($values.AZURE_SUBSCRIPTION_ID)/resourceGroups/$expectedGroup"
if ($group.id -ine $expectedId -or $group.name -ine $expectedGroup `
    -or $group.tags.'azd-env-name' -cne $values.AZURE_ENV_NAME `
    -or $group.tags.purpose -cne 'evidence-grounded-process') {
    throw 'Refusing teardown: the resource-group identity or ownership tags do not match.'
}
