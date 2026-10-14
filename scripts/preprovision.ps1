$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
. "$PSScriptRoot\environment.ps1"
if (-not $env:AZURE_LOCATION -or -not $env:AZURE_ENV_NAME) { throw 'Set an environment name and a customer-selected Azure location.' }
$approval = "create:$($env:AZURE_ENV_NAME):$($env:AZURE_SUBSCRIPTION_ID):$($env:AZURE_LOCATION)"
if ($env:WORKSPACE_DEPLOYMENT_APPROVAL -cne $approval) {
    throw 'Set process-only WORKSPACE_DEPLOYMENT_APPROVAL to create:<environment>:<subscription>:<location> after approving resource and role changes.'
}
& azd auth login --check-status | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'AZD authentication is unavailable. Sign in separately.' }
& dotnet build tools\Workspace.Tools\Workspace.Tools.csproj --no-restore --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Restore dependencies and resolve the tooling build before provisioning.' }
& dotnet run --project tools\Workspace.Tools --no-build -- check-approval
if ($LASTEXITCODE -ne 0) { throw 'The profile requires review and matching approval.' }
$models = Get-Content config\models.json -Raw | ConvertFrom-Json
if (@($models.profiles | Where-Object { $_.enabled -and $_.provider -eq 'anthropic' }).Count -gt 0) {
    foreach ($name in @('CLAUDE_ORGANIZATION_NAME', 'CLAUDE_COUNTRY_CODE', 'CLAUDE_INDUSTRY')) {
        if (-not [Environment]::GetEnvironmentVariable($name)) { throw "Set $name accurately before selecting a Claude deployment." }
    }
    if ($env:CLAUDE_TERMS_ACCEPTED -ne 'true') { throw 'Review the provider terms and explicitly set CLAUDE_TERMS_ACCEPTED=true before deployment.' }
}
$principal = & az ad signed-in-user show --query id --output tsv
if ($LASTEXITCODE -ne 0 -or -not $principal) { throw 'Cannot resolve the signed-in user in the resource tenant.' }
& azd env set AZURE_PRINCIPAL_ID $principal | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Cannot save the selected principal.' }
& azd env set AZURE_TENANT_ID $account.tenantId | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Cannot save the selected tenant.' }
& dotnet run --project tools\Workspace.Tools --no-build -- parameters
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve deployment configuration.' }
$parameters = Get-Content infra\resolved.parameters.json -Raw | ConvertFrom-Json -AsHashtable
$parameters.parameters.environmentName = @{ value = $env:AZURE_ENV_NAME }
$parameters.parameters.location = @{ value = $env:AZURE_LOCATION }
$parameters.parameters.principalId = @{ value = $principal }
foreach ($setting in @(
    @{ Parameter = 'claudeOrganizationName'; Environment = 'CLAUDE_ORGANIZATION_NAME' },
    @{ Parameter = 'claudeCountryCode'; Environment = 'CLAUDE_COUNTRY_CODE' },
    @{ Parameter = 'claudeIndustry'; Environment = 'CLAUDE_INDUSTRY' }
)) {
    $value = [Environment]::GetEnvironmentVariable($setting.Environment)
    if ($value) { $parameters.parameters[$setting.Parameter] = @{ value = $value } }
}
$parameters | ConvertTo-Json -Depth 30 | Set-Content infra\main.parameters.json -Encoding utf8
& az bicep build --file infra\main.bicep --stdout --no-restore | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Bicep compilation failed.' }
& dotnet run --project tools\Workspace.Tools --no-build -- manifests
if ($LASTEXITCODE -ne 0) { throw 'Runtime manifest generation failed.' }
