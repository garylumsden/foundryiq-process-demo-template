import { ModelConfig } from 'types.bicep'

param environmentName string
param location string
param principalId string
param models ModelConfig[]
param knowledgeBase string
param connectionName string
param searchApiVersion string
param resourcePrefix string
param searchSku string
param storageSku string
param logRetentionDays int
param claudeOrganizationName string
param claudeCountryCode string
param claudeIndustry string

var suffix = uniqueString(resourceGroup().id)
var tags = {
  'azd-env-name': environmentName
  purpose: 'evidence-grounded-process'
}
resource foundry 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: '${resourcePrefix}-ai-${suffix}'
  location: location
  kind: 'AIServices'
  sku: { name: 'S0' }
  tags: tags
  identity: { type: 'SystemAssigned' }
  properties: {
    customSubDomainName: '${resourcePrefix}-ai-${suffix}'
    allowProjectManagement: true
    disableLocalAuth: true
    publicNetworkAccess: 'Enabled'
  }
}
@batchSize(1)
resource modelDeployments 'Microsoft.CognitiveServices/accounts/deployments@2026-05-01' = [
  for model in models: if (model.enabled) {
    parent: foundry
    name: model.deployment
    sku: { name: model.sku, capacity: model.capacity }
    properties: {
      model: {
        format: model.provider == 'anthropic' ? 'Anthropic' : 'OpenAI'
        name: model.model
        version: model.version
      }
      versionUpgradeOption: 'NoAutoUpgrade'
      // The documented provider attestation is not present in the bundled deployment schema.
      #disable-next-line BCP037
      modelProviderData: model.provider == 'anthropic'
        ? {
            organizationName: claudeOrganizationName
            countryCode: claudeCountryCode
            industry: claudeIndustry
          }
        : null
    }
  }
]
resource project 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' = {
  parent: foundry
  name: 'process-workspace'
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    displayName: 'Process Workspace'
    description: 'Evidence-grounded assistance through Foundry IQ.'
  }
  dependsOn: [modelDeployments]
}
resource search 'Microsoft.Search/searchServices@2025-05-01' = {
  name: '${resourcePrefix}-search-${suffix}'
  location: location
  tags: tags
  sku: { name: searchSku }
  identity: { type: 'SystemAssigned' }
  properties: {
    replicaCount: 1
    partitionCount: 1
    hostingMode: 'Default'
    disableLocalAuth: true
    publicNetworkAccess: 'enabled'
    semanticSearch: 'free'
  }
}
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'ws${suffix}'
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: storageSku }
  properties: {
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
    publicNetworkAccess: 'Enabled'
  }
}
resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${resourcePrefix}-logs-${suffix}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: logRetentionDays
  }
}
resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${resourcePrefix}-insights-${suffix}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
    DisableLocalAuth: true
  }
}
resource foundryAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for role in [
    '53ca6127-db72-4b80-b1b0-d745d6d5456d'
    'eadc314b-1a2d-4efa-be10-5d325db5065e'
    '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
  ]: {
    name: guid(foundry.id, principalId, role)
    scope: foundry
    properties: {
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', role)
      principalId: principalId
      principalType: 'User'
    }
  }
]
resource userSearch 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for role in [
    '7ca78c08-252a-4471-8644-bb5ff32d4ba0'
    '8ebe5a00-799e-43f5-93ac-243d3dce84a7'
  ]: {
    name: guid(search.id, principalId, role)
    scope: search
    properties: {
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', role)
      principalId: principalId
      principalType: 'User'
    }
  }
]
resource projectSearch 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for role in [
    '1407120a-92aa-4202-b7e9-c0e197c71c8f'
    '7ca78c08-252a-4471-8644-bb5ff32d4ba0'
  ]: {
    name: guid(search.id, project.id, role)
    scope: search
    properties: {
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', role)
      principalId: project.identity.principalId
      principalType: 'ServicePrincipal'
    }
  }
]
resource searchAi 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for role in [
    '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
    'a97b65f3-24c7-4388-baec-2e87135dc908'
  ]: {
    name: guid(foundry.id, search.id, role)
    scope: foundry
    properties: {
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', role)
      principalId: search.identity.principalId
      principalType: 'ServicePrincipal'
    }
  }
]
resource userStorage 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, principalId, 'source-content')
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
    )
    principalId: principalId
    principalType: 'User'
  }
}
resource searchStorage 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, search.id, 'source-ingestion')
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
    )
    principalId: search.identity.principalId
    principalType: 'ServicePrincipal'
  }
}
resource telemetryAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(insights.id, principalId, 'telemetry')
  scope: insights
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '3913510d-42f4-4e42-8a64-420c390055eb'
    )
    principalId: principalId
    principalType: 'User'
  }
}
resource connection 'Microsoft.CognitiveServices/accounts/projects/connections@2025-09-01' = {
  parent: project
  name: connectionName
  properties: {
    category: 'RemoteTool'
    #disable-next-line BCP036
    authType: 'ProjectManagedIdentity'
    audience: 'https://search.azure.com/'
    target: 'https://${search.name}.search.windows.net/knowledgebases/${knowledgeBase}/mcp?api-version=${searchApiVersion}'
    isSharedToAll: true
    metadata: {
      ApiType: 'Azure'
      ResourceId: search.id
      location: location
    }
  }
}
output foundryEndpoint string = 'https://${foundry.name}.openai.azure.com'
output projectEndpoint string = 'https://${foundry.name}.services.ai.azure.com/api/projects/${project.name}'
output searchEndpoint string = 'https://${search.name}.search.windows.net'
output storageEndpoint string = storage.properties.primaryEndpoints.blob
output storageId string = storage.id
@secure()
output insightsConnectionString string = insights.properties.ConnectionString
