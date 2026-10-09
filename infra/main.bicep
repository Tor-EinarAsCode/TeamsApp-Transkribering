targetScope = 'resourceGroup'
param location string = resourceGroup().location
@minLength(3)
@maxLength(12)
param prefix string
param tenantId string
param clientId string
param mediaClientId string
param entraAdminObjectId string
param entraAdminName string
param senderMailbox string
param mediaFqdn string
param certificateThumbprint string
param vmAdminUsername string = 'teamsagentadmin'
@secure()
param vmAdminPassword string
@secure()
param clientSecret string
param openAiLocation string = location
param openAiModelName string = 'gpt-4o-mini'
param openAiModelVersion string = '2024-07-18'
param openAiDeploymentName string = 'minutes'
param vmSize string = 'Standard_D4s_v5'
var suffix = uniqueString(resourceGroup().id, prefix)
var sqlName = '${prefix}-sql-${suffix}'
var kvName = take('${prefix}-kv-${suffix}', 24)
var apiName = '${prefix}-api-${suffix}'
var sqlSuffix = environment().suffixes.sqlServerHostname
resource nsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' = {
 name: '${prefix}-media-nsg'
 location: location
 properties: {
  securityRules: [
   { name: 'HTTPS', properties: { priority: 100, access: 'Allow', direction: 'Inbound', protocol: 'Tcp', sourcePortRange: '*', destinationPortRange: '443', sourceAddressPrefix: '*', destinationAddressPrefix: '*' } }
   { name: 'Media', properties: { priority: 110, access: 'Allow', direction: 'Inbound', protocol: '*', sourcePortRange: '*', destinationPortRange: '8445', sourceAddressPrefix: '*', destinationAddressPrefix: '*' } }
  ]
 }
}
resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
 name: '${prefix}-vnet'
 location: location
 properties: {
  addressSpace: { addressPrefixes: ['10.40.0.0/16'] }
  subnets: [
   { name: 'media', properties: { addressPrefix: '10.40.1.0/24', networkSecurityGroup: { id: nsg.id } } }
   { name: 'api', properties: { addressPrefix: '10.40.2.0/24', delegations: [{ name: 'web', properties: { serviceName: 'Microsoft.Web/serverFarms' } }] } }
   { name: 'private', properties: { addressPrefix: '10.40.3.0/24', privateEndpointNetworkPolicies: 'Disabled' } }
  ]
 }
}
resource pip 'Microsoft.Network/publicIPAddresses@2024-05-01' = { name: '${prefix}-media-ip', location: location, sku: { name: 'Standard' }, properties: { publicIPAllocationMethod: 'Static' } }
resource nic 'Microsoft.Network/networkInterfaces@2024-05-01' = {
 name: '${prefix}-media-nic'
 location: location
 properties: { ipConfigurations: [{ name: 'main', properties: { privateIPAllocationMethod: 'Dynamic', subnet: { id: '${vnet.id}/subnets/media' }, publicIPAddress: { id: pip.id } } }] }
}
resource vm 'Microsoft.Compute/virtualMachines@2024-07-01' = {
 name: '${prefix}-media'
 location: location
 identity: { type: 'SystemAssigned' }
 properties: {
  hardwareProfile: { vmSize: vmSize }
  osProfile: { computerName: take('${prefix}-media', 15), adminUsername: vmAdminUsername, adminPassword: vmAdminPassword, windowsConfiguration: { provisionVMAgent: true, enableAutomaticUpdates: true } }
  storageProfile: { imageReference: { publisher: 'MicrosoftWindowsServer', offer: 'WindowsServer', sku: '2022-datacenter-azure-edition', version: 'latest' }, osDisk: { createOption: 'FromImage', managedDisk: { storageAccountType: 'Premium_LRS' } } }
  networkProfile: { networkInterfaces: [{ id: nic.id }] }
 }
}
resource sql 'Microsoft.Sql/servers@2023-08-01' = {
 name: sqlName
 location: location
 properties: { version: '12.0', minimalTlsVersion: '1.2', publicNetworkAccess: 'Disabled', administrators: { administratorType: 'ActiveDirectory', principalType: 'User', login: entraAdminName, sid: entraAdminObjectId, tenantId: tenantId, azureADOnlyAuthentication: true } }
}
resource database 'Microsoft.Sql/servers/databases@2023-08-01' = { parent: sql, name: 'teamsagent', location: location, sku: { name: 'S0', tier: 'Standard' }, properties: { maxSizeBytes: 268435456000 } }
resource privateSql 'Microsoft.Network/privateEndpoints@2024-05-01' = {
 name: '${prefix}-sql-private'
 location: location
 properties: { subnet: { id: '${vnet.id}/subnets/private' }, privateLinkServiceConnections: [{ name: 'sql', properties: { privateLinkServiceId: sql.id, groupIds: ['sqlServer'] } }] }
}
resource dns 'Microsoft.Network/privateDnsZones@2024-06-01' = { name: 'privatelink.${sqlSuffix}', location: 'global' }
resource dnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = { parent: dns, name: '${prefix}-link', location: 'global', properties: { virtualNetwork: { id: vnet.id }, registrationEnabled: false } }
resource sqlDns 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = { parent: privateSql, name: 'default', properties: { privateDnsZoneConfigs: [{ name: 'sql', properties: { privateDnsZoneId: dns.id } }] } }
resource speech 'Microsoft.CognitiveServices/accounts@2024-10-01' = { name: '${prefix}-speech-${suffix}', location: location, kind: 'SpeechServices', sku: { name: 'S0' }, properties: { publicNetworkAccess: 'Enabled' } }
resource ai 'Microsoft.CognitiveServices/accounts@2024-10-01' = { name: '${prefix}-ai-${suffix}', location: openAiLocation, kind: 'OpenAI', sku: { name: 'S0' }, properties: { customSubDomainName: '${prefix}-ai-${suffix}', publicNetworkAccess: 'Enabled' } }
resource model 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = { parent: ai, name: openAiDeploymentName, sku: { name: 'Standard', capacity: 10 }, properties: { model: { format: 'OpenAI', name: openAiModelName, version: openAiModelVersion }, versionUpgradeOption: 'OnceNewDefaultVersionAvailable' } }
resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = { name: kvName, location: location, properties: { tenantId: tenantId, sku: { family: 'A', name: 'standard' }, enableRbacAuthorization: true, enableSoftDelete: true, enablePurgeProtection: true, softDeleteRetentionInDays: 7 } }
resource botSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = { parent: vault, name: 'bot-client-secret', properties: { value: clientSecret } }
resource aiSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = { parent: vault, name: 'openai-key', properties: { value: ai.listKeys().key1 } }
resource speechSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = { parent: vault, name: 'speech-key', properties: { value: speech.listKeys().key1 } }
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = { name: '${prefix}-plan', location: location, kind: 'linux', sku: { name: 'S1', tier: 'Standard' }, properties: { reserved: true } }
resource api 'Microsoft.Web/sites@2023-12-01' = {
 name: apiName
 location: location
 kind: 'app,linux'
 identity: { type: 'SystemAssigned' }
 properties: { serverFarmId: plan.id, httpsOnly: true, virtualNetworkSubnetId: '${vnet.id}/subnets/api', siteConfig: { linuxFxVersion: 'DOTNETCORE|8.0', alwaysOn: true, minTlsVersion: '1.2', ftpsState: 'Disabled', appSettings: [
  { name: 'TenantId', value: tenantId }
  { name: 'ClientId', value: clientId }
  { name: 'ClientSecret', value: '@Microsoft.KeyVault(SecretUri=${vault.properties.vaultUri}secrets/bot-client-secret/)' }
  { name: 'ConnectionStrings__Sql', value: 'Server=tcp:${sql.name}.${sqlSuffix},1433;Database=teamsagent;Authentication=Active Directory Managed Identity;Encrypt=True;' }
  { name: 'OpenAI__Endpoint', value: 'https://${ai.name}.openai.azure.com/' }
  { name: 'OpenAI__Deployment', value: openAiDeploymentName }
  { name: 'OpenAI__ApiKey', value: '@Microsoft.KeyVault(SecretUri=${vault.properties.vaultUri}secrets/openai-key/)' }
  { name: 'Mail__Sender', value: senderMailbox }
  { name: 'Media__Endpoint', value: 'https://${mediaFqdn}' }
  { name: 'Media__Audience', value: 'api://${mediaClientId}' }
  { name: 'Jobs__Enabled', value: 'false' }
 ] } }
}
resource apiVaultRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = { scope: vault, name: guid(vault.id, api.id, 'secret-user'), properties: { roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions','4633458b-17de-408a-b874-0445c86b69e6'), principalId: api.identity.principalId, principalType: 'ServicePrincipal' } }
resource vmVaultRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = { scope: vault, name: guid(vault.id, vm.id, 'secret-user'), properties: { roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions','4633458b-17de-408a-b874-0445c86b69e6'), principalId: vm.identity.principalId, principalType: 'ServicePrincipal' } }
resource bot 'Microsoft.BotService/botServices@2022-09-15' = { name: '${prefix}-bot-${suffix}', location: 'global', kind: 'azurebot', sku: { name: 'F0' }, properties: { displayName: 'Intern møteagent', endpoint: 'https://${api.properties.defaultHostName}/api/messages', msaAppId: clientId, msaAppTenantId: tenantId, msaAppType: 'SingleTenant' } }
resource teamsChannel 'Microsoft.BotService/botServices/channels@2022-09-15' = { parent: bot, name: 'MsTeamsChannel', location: 'global', properties: { channelName: 'MsTeamsChannel', properties: { isEnabled: true, enableCalling: true, callingWebhook: 'https://${mediaFqdn}/graph/callback' } } }
output apiHost string = api.properties.defaultHostName
output mediaPublicIp string = pip.properties.ipAddress
output vaultName string = vault.name
output sqlServer string = sql.name
output apiIdentity string = api.identity.principalId
output mediaIdentity string = vm.identity.principalId
output mediaCertificateThumbprint string = certificateThumbprint
output speechRegion string = location
