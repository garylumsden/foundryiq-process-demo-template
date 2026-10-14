targetScope = 'subscription'

import { ModelConfig } from 'types.bicep'

@minLength(3)
@maxLength(24)
param environmentName string
param location string
param principalId string
param models ModelConfig[]
param knowledgeBase string
param connectionName string
param searchApiVersion string
param resourcePrefix string = 'workspace'
param searchSku string = 'basic'
param storageSku string = 'Standard_LRS'
@minValue(30)
@maxValue(730)
param logRetentionDays int = 30
param claudeOrganizationName string = ''
param claudeCountryCode string = ''
param claudeIndustry string = ''

resource group 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: {
    'azd-env-name': environmentName
    purpose: 'evidence-grounded-process'
  }
}

module resources 'resources.bicep' = {
  scope: group
  params: {
    environmentName: environmentName
    location: location
    principalId: principalId
    models: models
    knowledgeBase: knowledgeBase
    connectionName: connectionName
    searchApiVersion: searchApiVersion
    resourcePrefix: resourcePrefix
    searchSku: searchSku
    storageSku: storageSku
    logRetentionDays: logRetentionDays
    claudeOrganizationName: claudeOrganizationName
    claudeCountryCode: claudeCountryCode
    claudeIndustry: claudeIndustry
  }
}

output AZURE_RESOURCE_GROUP string = group.name
output AZURE_TENANT_ID string = tenant().tenantId
output AZURE_PRINCIPAL_ID string = principalId
output FOUNDRY_ENDPOINT string = resources.outputs.foundryEndpoint
output FOUNDRY_PROJECT_ENDPOINT string = resources.outputs.projectEndpoint
output SEARCH_ENDPOINT string = resources.outputs.searchEndpoint
output STORAGE_ENDPOINT string = resources.outputs.storageEndpoint
output AZURE_STORAGE_RESOURCE_ID string = resources.outputs.storageId
@secure()
output APPLICATIONINSIGHTS_CONNECTION_STRING string = resources.outputs.insightsConnectionString
