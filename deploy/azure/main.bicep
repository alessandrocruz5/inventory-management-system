// Azure deployment (S14): hand-written IaC, no Aspire Azure packages (plan D1).
// Mirrors deploy/docker-compose.yaml, which S13 verified. deploy.sh deploys it; never run it by hand with secrets on the command line.
targetScope = 'resourceGroup'

@description('Region for every resource.')
param location string = resourceGroup().location

@description('Prefix for resource names.')
param namePrefix string = 'ims'

@description('API image (ghcr.io/<owner>/inventory-api:<tag>), used by api, migrate and seed (plan D4).')
param apiImage string

@description('Web image (ghcr.io/<owner>/inventory-web:<tag>): nginx serving the SPA and proxying /api/ to api (plan D2).')
param webImage string

@description('Azure SQL server admin login, used only by the migrate job (plan D6).')
param sqlAdminLogin string

@secure()
@description('Azure SQL server admin password. Letters and digits only: it sits inside a connection string.')
param sqlAdminPassword string

@secure()
@description('Password of the contained user inventory_app, which migrate creates and api and seed connect as.')
param inventoryAppPassword string

@secure()
@description('Password of the seeded sign-in users.')
param seedPassword string

var databaseName = 'inventory'

// ---- Observability (plan D10: 30-day retention, 1 GB/day cap) ----

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${namePrefix}-logs'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: 1
    }
  }
}

// ---- Container Apps environment (Consumption) ----

resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${namePrefix}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

// ---- Azure SQL (SQL authentication, plan D6; free offer, plan D10) ----

resource sqlServer 'Microsoft.Sql/servers@2023-08-01' = {
  name: '${namePrefix}-sql-${uniqueString(resourceGroup().id)}'
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// Plan D11: lets Azure services, including Container Apps' outbound IPs, reach the server.
resource sqlFirewallAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// Free offer: AutoPause when the monthly allowance runs out. Never BillOverUsage: it can't be switched back.
resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: databaseName
  location: location
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    autoPauseDelay: 60
    minCapacity: json('0.5')
  }
}

// Connection strings live only in Container Apps secrets. The 60 s timeout covers resuming a paused database.
var sqlFqdn = sqlServer.properties.fullyQualifiedDomainName
var adminConnectionString = 'Server=tcp:${sqlFqdn},1433;Database=${databaseName};User ID=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60'
var appConnectionString = 'Server=tcp:${sqlFqdn},1433;Database=${databaseName};User ID=inventory_app;Password=${inventoryAppPassword};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60'

var httpScale = {
  minReplicas: 0
  maxReplicas: 1
  rules: [
    {
      name: 'http'
      http: {
        metadata: {
          concurrentRequests: '20'
        }
      }
    }
  ]
}

// ---- api: internal ingress; nginx in web resolves it as http://api (plan D2) ----

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'api'
  location: location
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        targetPort: 8080
        transport: 'http'
        allowInsecure: true
      }
      secrets: [
        {
          name: 'app-connection-string'
          value: appConnectionString
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              // The API connects as inventory_app, never as the server admin.
              name: 'ConnectionStrings__inventory'
              secretRef: 'app-connection-string'
            }
            {
              name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED'
              value: 'true'
            }
          ]
        }
      ]
      scale: httpScale
    }
  }
  dependsOn: [
    sqlDatabase
    sqlFirewallAzure
  ]
}

// ---- web: external ingress; the image's own entrypoint renders the nginx template ----

resource web 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'web'
  location: location
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'web'
          image: webImage
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            {
              name: 'PORT'
              value: '8080'
            }
            {
              name: 'services__api__http__0'
              value: 'http://api'
            }
          ]
        }
      ]
      scale: httpScale
    }
  }
  // nginx resolves api when it starts.
  dependsOn: [
    api
  ]
}

// ---- Jobs: manual trigger (plan D7), started by deploy.sh ----

resource migrateJob 'Microsoft.App/jobs@2024-03-01' = {
  name: 'migrate'
  location: location
  properties: {
    environmentId: env.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 900
      replicaRetryLimit: 0
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      secrets: [
        {
          name: 'admin-connection-string'
          value: adminConnectionString
        }
        {
          name: 'inventory-app-password'
          value: inventoryAppPassword
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'migrate'
          image: apiImage
          args: [ 'migrate' ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              // Only migrate connects as the server admin; it creates the contained user inventory_app.
              name: 'ConnectionStrings__inventory'
              secretRef: 'admin-connection-string'
            }
            {
              name: 'InventoryApp__Password'
              secretRef: 'inventory-app-password'
            }
          ]
        }
      ]
    }
  }
  dependsOn: [
    sqlDatabase
    sqlFirewallAzure
  ]
}

resource seedJob 'Microsoft.App/jobs@2024-03-01' = {
  name: 'seed'
  location: location
  properties: {
    environmentId: env.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 900
      replicaRetryLimit: 0
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      secrets: [
        {
          name: 'app-connection-string'
          value: appConnectionString
        }
        {
          name: 'seed-password'
          value: seedPassword
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'seed'
          image: apiImage
          args: [ 'seed' ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'ConnectionStrings__inventory'
              secretRef: 'app-connection-string'
            }
            {
              name: 'Seed__Password'
              secretRef: 'seed-password'
            }
          ]
        }
      ]
    }
  }
  dependsOn: [
    sqlDatabase
    sqlFirewallAzure
  ]
}

output webUrl string = 'https://${web.properties.configuration.ingress.fqdn}'
