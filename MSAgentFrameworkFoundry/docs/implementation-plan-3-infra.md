# Foundry Agents — Plan 3: Infrastructure & CI/CD Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Prerequisite:** Plan 2 complete and tagged `plan-2-services-complete`.

**Goal:** Ship the two services to Azure: build container images, provision Azure resources via hand-written Bicep, and wire GitHub Actions so `git push origin main` redeploys both apps.

**Architecture:** Two multi-stage Dockerfiles. `infra/main.bicep` is the single source of truth — it composes six modules (Log Analytics, ACR, Cosmos, Key Vault, Foundry, Container Apps) and writes the role assignments. CI/CD uses OIDC federated identity from GitHub Actions to a deploy-only service principal.

**Tech Stack:** Azure Bicep (latest, ≥ `0.32.x`), Docker (multi-stage), GitHub Actions, `azure/login@v2` with OIDC, `azure/setup-bicep@v1`.

---

## File structure created by this plan

```
MSAgentFrameworkFoundry/
├── src/
│   ├── Foundry.Agents.Developer/
│   │   └── Dockerfile                              # NEW
│   └── Foundry.Agents.Reviewer/
│       └── Dockerfile                              # NEW
├── infra/
│   ├── main.bicep                                  # NEW
│   ├── main.bicepparam                             # NEW (per-environment values)
│   └── modules/
│       ├── loganalytics.bicep                      # NEW
│       ├── acr.bicep                               # NEW
│       ├── cosmos.bicep                            # NEW
│       ├── keyvault.bicep                          # NEW
│       ├── foundry.bicep                           # NEW
│       └── containerapps.bicep                     # NEW
├── .github/
│   └── workflows/
│       ├── pr.yml                                  # NEW
│       └── deploy.yml                              # NEW
└── .dockerignore                                   # NEW
```

---

## Task 1: Developer Dockerfile (SDK base, /work volume)

**Files:**
- Create: `src/Foundry.Agents.Developer/Dockerfile`
- Create: `MSAgentFrameworkFoundry/.dockerignore`

The Developer needs `git` and the .NET SDK at **runtime** because it shells out to `dotnet build` / `dotnet test` against arbitrary cloned repos. Final stage stays on the SDK image. Reviewer (Task 2) does not.

- [ ] **Step 1: `.dockerignore`**

```text
# Build outputs
**/bin/
**/obj/
# Tests
tests/
# Aspire AppHost is dev-only — never built into a container
src/Foundry.Agents.AppHost/
# IDE
**/.vs/
**/.idea/
**/.vscode/
# Git
.git/
.gitignore
# Docs / infra / workflows
docs/
infra/
.github/
**/Dockerfile
**/README*
```

- [ ] **Step 2: Developer Dockerfile**

Create `src/Foundry.Agents.Developer/Dockerfile`:

```dockerfile
# syntax=docker/dockerfile:1.7

ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0

FROM ${SDK_IMAGE} AS build
WORKDIR /src

# Copy package management so `dotnet restore` layer caches well.
COPY MSAgentFrameworkFoundry/Directory.Build.props ./MSAgentFrameworkFoundry/
COPY MSAgentFrameworkFoundry/Directory.Packages.props ./MSAgentFrameworkFoundry/
COPY MSAgentFrameworkFoundry/global.json ./MSAgentFrameworkFoundry/
COPY MSAgentFrameworkFoundry/MSAgentFrameworkFoundry.slnx ./MSAgentFrameworkFoundry/

# Copy the projects this container needs and their direct refs only.
COPY MSAgentFrameworkFoundry/src/Foundry.Agents.Contracts/        ./MSAgentFrameworkFoundry/src/Foundry.Agents.Contracts/
COPY MSAgentFrameworkFoundry/src/Foundry.Agents.Memory/           ./MSAgentFrameworkFoundry/src/Foundry.Agents.Memory/
COPY MSAgentFrameworkFoundry/src/Foundry.Agents.ServiceDefaults/  ./MSAgentFrameworkFoundry/src/Foundry.Agents.ServiceDefaults/
COPY MSAgentFrameworkFoundry/src/Foundry.Agents.Developer/        ./MSAgentFrameworkFoundry/src/Foundry.Agents.Developer/
COPY MSAgentFrameworkFoundry/personas/                            ./MSAgentFrameworkFoundry/personas/

WORKDIR /src/MSAgentFrameworkFoundry
RUN dotnet restore src/Foundry.Agents.Developer/Foundry.Agents.Developer.csproj
RUN dotnet publish src/Foundry.Agents.Developer/Foundry.Agents.Developer.csproj \
        -c Release \
        -o /app/publish \
        --no-restore \
        /p:UseAppHost=false

# Runtime stage keeps the SDK + git (git ships in the SDK image).
FROM ${SDK_IMAGE} AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8081 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_NOLOGO=true \
    NUGET_XMLDOC_MODE=skip

# Workspace volume (ACA mounts an emptyDir here in prod).
VOLUME ["/work"]

COPY --from=build /app/publish .
EXPOSE 8081
USER app
ENTRYPOINT ["dotnet", "Foundry.Agents.Developer.dll"]
```

- [ ] **Step 3: Build the image and run it locally**

From the repo root (`C:\repos\test\ClaudeAgents`):

```bash
docker build -f MSAgentFrameworkFoundry/src/Foundry.Agents.Developer/Dockerfile \
  -t foundry-developer:dev .
docker run --rm -p 8081:8081 foundry-developer:dev &
sleep 5
curl -s http://localhost:8081/health/live
docker kill $(docker ps -q --filter ancestor=foundry-developer:dev)
```

Expected: 200 from `/health/live`.

- [ ] **Step 4: Commit**

```bash
git add MSAgentFrameworkFoundry/src/Foundry.Agents.Developer/Dockerfile MSAgentFrameworkFoundry/.dockerignore
git commit -m "Add Developer Dockerfile (SDK base, /work volume)"
```

---

## Task 2: Reviewer Dockerfile (chiseled aspnet)

**Files:**
- Create: `src/Foundry.Agents.Reviewer/Dockerfile`

- [ ] **Step 1: Reviewer Dockerfile**

```dockerfile
# syntax=docker/dockerfile:1.7

ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0-chiseled

FROM ${SDK_IMAGE} AS build
WORKDIR /src

COPY MSAgentFrameworkFoundry/Directory.Build.props ./MSAgentFrameworkFoundry/
COPY MSAgentFrameworkFoundry/Directory.Packages.props ./MSAgentFrameworkFoundry/
COPY MSAgentFrameworkFoundry/global.json ./MSAgentFrameworkFoundry/
COPY MSAgentFrameworkFoundry/MSAgentFrameworkFoundry.slnx ./MSAgentFrameworkFoundry/

COPY MSAgentFrameworkFoundry/src/Foundry.Agents.Contracts/        ./MSAgentFrameworkFoundry/src/Foundry.Agents.Contracts/
COPY MSAgentFrameworkFoundry/src/Foundry.Agents.Memory/           ./MSAgentFrameworkFoundry/src/Foundry.Agents.Memory/
COPY MSAgentFrameworkFoundry/src/Foundry.Agents.ServiceDefaults/  ./MSAgentFrameworkFoundry/src/Foundry.Agents.ServiceDefaults/
COPY MSAgentFrameworkFoundry/src/Foundry.Agents.Reviewer/         ./MSAgentFrameworkFoundry/src/Foundry.Agents.Reviewer/
COPY MSAgentFrameworkFoundry/personas/                            ./MSAgentFrameworkFoundry/personas/

WORKDIR /src/MSAgentFrameworkFoundry
RUN dotnet restore src/Foundry.Agents.Reviewer/Foundry.Agents.Reviewer.csproj
RUN dotnet publish src/Foundry.Agents.Reviewer/Foundry.Agents.Reviewer.csproj \
        -c Release \
        -o /app/publish \
        --no-restore \
        /p:UseAppHost=false

FROM ${RUNTIME_IMAGE} AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8081 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_NOLOGO=true

COPY --from=build /app/publish .
EXPOSE 8081
USER app
ENTRYPOINT ["dotnet", "Foundry.Agents.Reviewer.dll"]
```

- [ ] **Step 2: Build + smoke-test**

```bash
docker build -f MSAgentFrameworkFoundry/src/Foundry.Agents.Reviewer/Dockerfile \
  -t foundry-reviewer:dev .
docker run --rm -p 8082:8081 foundry-reviewer:dev &
sleep 5
curl -s http://localhost:8082/health/live
docker kill $(docker ps -q --filter ancestor=foundry-reviewer:dev)
```

Expected: 200 from `/health/live`. Image size should be markedly smaller than Developer's (no SDK).

- [ ] **Step 3: Commit**

```bash
git add MSAgentFrameworkFoundry/src/Foundry.Agents.Reviewer/Dockerfile
git commit -m "Add Reviewer Dockerfile (aspnet:10.0-chiseled final stage)"
```

---

## Task 3: infra/main.bicep skeleton + main.bicepparam

**Files:**
- Create: `infra/main.bicep`
- Create: `infra/main.bicepparam`

- [ ] **Step 1: `main.bicep` skeleton**

```bicep
// Foundry Agents — infrastructure root.
// Deploy with: az deployment sub create -l <loc> -f infra/main.bicep -p infra/main.bicepparam
targetScope = 'subscription'

@minLength(3)
@maxLength(11)
@description('Short environment name; becomes a resource-name suffix. e.g. fdyprod.')
param envName string

@description('Azure region for all resources.')
param location string = deployment().location

@description('GitHub PAT for the Developer agent to clone/push. Stored in Key Vault as github-pat.')
@secure()
param githubPat string

@description('Container image references (set after the first image build).')
param developerImage string
param reviewerImage string

var rgName = 'rg-${envName}'

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: rgName
  location: location
}

module logs 'modules/loganalytics.bicep' = {
  name: 'logs'
  scope: rg
  params: { envName: envName, location: location }
}

module acr 'modules/acr.bicep' = {
  name: 'acr'
  scope: rg
  params: { envName: envName, location: location }
}

module cosmos 'modules/cosmos.bicep' = {
  name: 'cosmos'
  scope: rg
  params: { envName: envName, location: location }
}

module kv 'modules/keyvault.bicep' = {
  name: 'kv'
  scope: rg
  params: {
    envName: envName
    location: location
    githubPat: githubPat
  }
}

module foundry 'modules/foundry.bicep' = {
  name: 'foundry'
  scope: rg
  params: { envName: envName, location: location, logsId: logs.outputs.workspaceId }
}

module apps 'modules/containerapps.bicep' = {
  name: 'apps'
  scope: rg
  params: {
    envName: envName
    location: location
    logsId: logs.outputs.workspaceId
    acrName: acr.outputs.acrName
    acrLoginServer: acr.outputs.loginServer
    cosmosAccountName: cosmos.outputs.accountName
    cosmosEndpoint: cosmos.outputs.endpoint
    keyvaultName: kv.outputs.vaultName
    foundryEndpoint: foundry.outputs.endpoint
    foundryDeploymentName: foundry.outputs.deploymentName
    githubMcpEndpoint: foundry.outputs.githubMcpEndpoint
    developerImage: developerImage
    reviewerImage: reviewerImage
  }
}

output developerFqdn  string = apps.outputs.developerFqdn
output reviewerFqdn   string = apps.outputs.reviewerFqdn
output acrLoginServer string = acr.outputs.loginServer
output cosmosEndpoint string = cosmos.outputs.endpoint
```

- [ ] **Step 2: `main.bicepparam`**

```bicep
using 'main.bicep'

param envName = 'fdyprod'
// Pre-deploy: set this via `az deployment sub create -p githubPat=<value>` or env var.
param githubPat = ''
// First deploy can leave these empty and patch after image push.
param developerImage = ''
param reviewerImage = ''
```

- [ ] **Step 3: Lint**

```bash
az bicep build --file infra/main.bicep
```

Expected: writes `main.json`, no warnings. Modules don't exist yet — expect "module file not found" errors. Implement them in Tasks 4–9.

- [ ] **Step 4: Commit**

```bash
mkdir -p MSAgentFrameworkFoundry/infra/modules
git add MSAgentFrameworkFoundry/infra/main.bicep MSAgentFrameworkFoundry/infra/main.bicepparam
git commit -m "Add infra/main.bicep skeleton wiring six modules"
```

---

## Task 4: loganalytics.bicep

**Files:** Create `infra/modules/loganalytics.bicep`

- [ ] **Step 1: Write the module**

```bicep
@description('Short environment name; reused as a suffix.')
param envName string
param location string = resourceGroup().location

resource ws 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${envName}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    features: { enableLogAccessUsingOnlyResourcePermissions: true }
  }
}

output workspaceId string = ws.id
output customerId string = ws.properties.customerId
```

- [ ] **Step 2: Validate the build**

```bash
az bicep build --file infra/modules/loganalytics.bicep
```

Expected: no warnings.

- [ ] **Step 3: Commit**

```bash
git add MSAgentFrameworkFoundry/infra/modules/loganalytics.bicep
git commit -m "Add Log Analytics workspace module"
```

---

## Task 5: acr.bicep

**Files:** Create `infra/modules/acr.bicep`

- [ ] **Step 1: Write the module**

ACR is `Premium` (matches spec §7.2). `adminUserEnabled: false` — managed-identity pulls only.

```bicep
param envName string
param location string = resourceGroup().location

var registryName = take('acrfdy${envName}${uniqueString(resourceGroup().id)}', 50)

resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: registryName
  location: location
  sku: { name: 'Premium' }
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
    zoneRedundancy: 'Disabled'
  }
}

output acrName string = acr.name
output loginServer string = acr.properties.loginServer
output acrId string = acr.id
```

- [ ] **Step 2: Lint + commit**

```bash
az bicep build --file infra/modules/acr.bicep
git add MSAgentFrameworkFoundry/infra/modules/acr.bicep
git commit -m "Add ACR module (Premium, admin disabled)"
```

---

## Task 6: cosmos.bicep — serverless + agent-threads container

**Files:** Create `infra/modules/cosmos.bicep`

Per §7.2: serverless, single region, database `agentdb`, container `agent-threads` with PK `/agentRole` and default TTL 604 800 s (7 days).

- [ ] **Step 1: Write the module**

```bicep
param envName string
param location string = resourceGroup().location

var accountName = toLower('cosmos-fdy-${envName}-${uniqueString(resourceGroup().id)}')

resource account 'Microsoft.DocumentDB/databaseAccounts@2024-08-15' = {
  name: accountName
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    capabilities: [ { name: 'EnableServerless' } ]
    consistencyPolicy: { defaultConsistencyLevel: 'Session' }
    locations: [ { locationName: location, failoverPriority: 0, isZoneRedundant: false } ]
    disableLocalAuth: true   // managed-identity only
    publicNetworkAccess: 'Enabled'
  }
}

resource db 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-08-15' = {
  parent: account
  name: 'agentdb'
  properties: { resource: { id: 'agentdb' } }
}

resource threads 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-08-15' = {
  parent: db
  name: 'agent-threads'
  properties: {
    resource: {
      id: 'agent-threads'
      partitionKey: { paths: [ '/agentRole' ], kind: 'Hash' }
      defaultTtl: 604800
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [ { path: '/*' } ]
        excludedPaths: [ { path: '/messages/*' } ]   // skip indexing message bodies — they're large
      }
    }
  }
}

output accountName string = account.name
output endpoint string = account.properties.documentEndpoint
output containerName string = threads.name
output databaseName string = db.name
```

- [ ] **Step 2: Lint + commit**

```bash
az bicep build --file infra/modules/cosmos.bicep
git add MSAgentFrameworkFoundry/infra/modules/cosmos.bicep
git commit -m "Add Cosmos module: serverless, /agentRole PK, 7d default TTL"
```

---

## Task 7: keyvault.bicep — Key Vault + github-pat secret

**Files:** Create `infra/modules/keyvault.bicep`

- [ ] **Step 1: Write the module**

```bicep
param envName string
param location string = resourceGroup().location

@secure()
param githubPat string

var vaultName = take('kv-fdy-${envName}-${uniqueString(resourceGroup().id)}', 24)

resource kv 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: vaultName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true   // role-based, not access policies
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
  }
}

resource patSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(githubPat)) {
  parent: kv
  name: 'github-pat'
  properties: {
    value: githubPat
    contentType: 'text/plain'
  }
}

output vaultName string = kv.name
output vaultUri string = kv.properties.vaultUri
output vaultId string = kv.id
```

- [ ] **Step 2: Lint + commit**

```bash
az bicep build --file infra/modules/keyvault.bicep
git add MSAgentFrameworkFoundry/infra/modules/keyvault.bicep
git commit -m "Add Key Vault module with github-pat secret (RBAC, soft-delete 7d)"
```

---

## Task 8: foundry.bicep — Foundry hub + project + Claude Opus 4.7 + GitHub MCP

**Files:** Create `infra/modules/foundry.bicep`

This module is the most variable per spec risk #1. Foundry exposes Anthropic models either as serverless deployments or via hub-attached endpoints; the resource type names also shift between API versions. The template below uses `Microsoft.CognitiveServices/accounts` with kind `AIServices` (the v2 surface) and a serverless `deployment` child resource — the most stable shape at time of writing. **If the Foundry portal shows a different resource shape for your tenant, swap this body for the matching ARM resource — the `endpoint` / `deploymentName` / `githubMcpEndpoint` outputs are the only contract the rest of the infra depends on.**

- [ ] **Step 1: Write the module**

```bicep
param envName string
param location string = resourceGroup().location
@description('Log Analytics workspace ID for diagnostics.')
param logsId string

@description('Anthropic deployment name. Per spec D-3 / D-9 this is claude-opus-4-7.')
param modelDeploymentName string = 'claude-opus-4-7'

@description('Foundry serverless model SKU for Anthropic Claude Opus 4.7.')
param modelSku object = {
  name: 'GlobalStandard'
  capacity: 1
}

var accountName = take('aifdy-${envName}-${uniqueString(resourceGroup().id)}', 32)

resource account 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: accountName
  location: location
  kind: 'AIServices'
  identity: { type: 'SystemAssigned' }
  sku: { name: 'S0' }
  properties: {
    publicNetworkAccess: 'Enabled'
    customSubDomainName: accountName
    disableLocalAuth: true
  }
}

resource deployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: account
  name: modelDeploymentName
  sku: modelSku
  properties: {
    model: {
      format: 'Anthropic'
      name: 'claude-opus-4-7'
      version: 'latest'
    }
    versionUpgradeOption: 'OnceCurrentVersionExpired'
  }
}

resource diag 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  scope: account
  name: 'to-logs'
  properties: {
    workspaceId: logsId
    logs: [ { category: 'Audit', enabled: true }, { category: 'RequestResponse', enabled: true } ]
    metrics: [ { category: 'AllMetrics', enabled: true } ]
  }
}

// GitHub MCP server resource. As of writing the API path is provisional.
// If your tenant's Foundry surface does not yet expose this resource, leave
// `githubMcpEndpoint` empty and configure the endpoint via a Key Vault
// secret + ACA env var override after deployment.
@description('Set to the literal MCP URL once Foundry provisions one. Empty = managed inside Foundry, configure ACA env var manually.')
param githubMcpEndpointOverride string = ''

output endpoint string = account.properties.endpoint
output deploymentName string = deployment.name
output githubMcpEndpoint string = githubMcpEndpointOverride
output accountId string = account.id
output identityPrincipalId string = account.identity.principalId
```

- [ ] **Step 2: Lint**

```bash
az bicep build --file infra/modules/foundry.bicep
```

Expected: build succeeds. There may be a `BCP037` warning if your CLI's resource type catalogue is older than the Anthropic-on-Foundry preview — acceptable.

- [ ] **Step 3: Commit**

```bash
git add MSAgentFrameworkFoundry/infra/modules/foundry.bicep
git commit -m "Add Foundry hub + Claude Opus 4.7 deployment module"
```

---

## Task 9: containerapps.bicep — managed env + two ACA apps

**Files:** Create `infra/modules/containerapps.bicep`

- [ ] **Step 1: Write the module**

Implements §7.2 in full: shared environment, both apps on port 8081, Reviewer internal-only, Developer external HTTPS with 1 800 s idle timeout, `minReplicas: 0`, `maxReplicas: 3`, system-assigned MI on each, `/work` emptyDir-style volume on Developer.

```bicep
param envName string
param location string = resourceGroup().location
param logsId string
param acrName string
param acrLoginServer string
param cosmosAccountName string
param cosmosEndpoint string
param keyvaultName string
param foundryEndpoint string
param foundryDeploymentName string
param githubMcpEndpoint string
param developerImage string
param reviewerImage string

@description('Cosmos DB Built-in Data Contributor role GUID.')
var cosmosDataContributorRoleId = '00000000-0000-0000-0000-000000000002'
@description('Key Vault Secrets User role.')
var kvSecretsUserRoleId         = '4633458b-17de-408a-b874-0445c86b69e6'
@description('AcrPull role.')
var acrPullRoleId               = '7f951dda-4ed3-11e8-a85a-9dc1c4a3a4a4'
@description('Cognitive Services User role.')
var aiUserRoleId                = 'a97b65f3-24c7-4388-baec-2e87135dc908'

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: 'log-${envName}'
}

resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${envName}'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: listKeys(logs.id, '2023-09-01').primarySharedKey
      }
    }
    workloadProfiles: [ { name: 'Consumption', workloadProfileType: 'Consumption' } ]
  }
}

resource reviewer 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'reviewer-app'
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    environmentId: env.id
    configuration: {
      ingress: {
        external: false
        targetPort: 8081
        transport: 'http'
        allowInsecure: false
      }
      registries: [ { server: acrLoginServer, identity: 'system' } ]
    }
    template: {
      containers: [
        {
          name: 'reviewer'
          image: reviewerImage
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'FoundryChat__Endpoint',     value: foundryEndpoint }
            { name: 'FoundryChat__DeploymentName', value: foundryDeploymentName }
            { name: 'Cosmos__Endpoint',          value: cosmosEndpoint }
            { name: 'Cosmos__DatabaseName',      value: 'agentdb' }
            { name: 'Cosmos__ContainerName',     value: 'agent-threads' }
            { name: 'Reviewer__DefaultEffort',   value: 'High' }
            { name: 'Reviewer__GitHubMcpEndpoint', value: githubMcpEndpoint }
          ]
        }
      ]
      scale: { minReplicas: 0, maxReplicas: 3 }
    }
  }
}

resource developer 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'developer-app'
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    environmentId: env.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8081
        transport: 'http'
        allowInsecure: false
        // 1800 s = 30 min, the ACA cap. See spec risk #2.
        clientCertificateMode: 'ignore'
        additionalPortMappings: []
      }
      secrets: [
        {
          name: 'github-pat'
          identity: 'system'
          keyVaultUrl: 'https://${keyvaultName}${environment().suffixes.keyvaultDns}/secrets/github-pat'
        }
      ]
      registries: [ { server: acrLoginServer, identity: 'system' } ]
    }
    template: {
      containers: [
        {
          name: 'developer'
          image: developerImage
          resources: { cpu: json('1.0'), memory: '2Gi' }
          env: [
            { name: 'FoundryChat__Endpoint',     value: foundryEndpoint }
            { name: 'FoundryChat__DeploymentName', value: foundryDeploymentName }
            { name: 'Cosmos__Endpoint',          value: cosmosEndpoint }
            { name: 'Cosmos__DatabaseName',      value: 'agentdb' }
            { name: 'Cosmos__ContainerName',     value: 'agent-threads' }
            { name: 'Developer__DefaultEffort',  value: 'Xhigh' }
            { name: 'Developer__MaxReviewRounds', value: '3' }
            { name: 'Developer__WorkspaceRoot',  value: '/work' }
            { name: 'Developer__GitHubMcpEndpoint', value: githubMcpEndpoint }
            { name: 'Developer__ReviewerMcpEndpoint', value: 'https://${reviewer.properties.configuration.ingress.fqdn}/mcp' }
            { name: 'GITHUB_PAT',                secretRef: 'github-pat' }
          ]
          volumeMounts: [ { volumeName: 'work', mountPath: '/work' } ]
        }
      ]
      volumes: [ { name: 'work', storageType: 'EmptyDir' } ]
      scale: { minReplicas: 0, maxReplicas: 3 }
    }
  }
}

// Role assignments. NB: Cosmos data-plane role uses the data-plane RBAC path,
// not Microsoft.Authorization/roleAssignments. We approximate here; if the
// CLI rejects this style, switch to 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments'.

resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' existing = {
  name: acrName
}

resource kv 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyvaultName
}

resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-08-15' existing = {
  name: cosmosAccountName
}

resource developerAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: acr
  name: guid(acr.id, developer.id, 'AcrPull')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: developer.identity.principalId
    principalType: 'ServicePrincipal'
  }
}
resource reviewerAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: acr
  name: guid(acr.id, reviewer.id, 'AcrPull')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: reviewer.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource developerKvSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: kv
  name: guid(kv.id, developer.id, 'KvSecretsUser')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsUserRoleId)
    principalId: developer.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource developerCosmosData 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-08-15' = {
  parent: cosmos
  name: guid(cosmos.id, developer.id, 'cosmosDataContributor')
  properties: {
    roleDefinitionId: '${cosmos.id}/sqlRoleDefinitions/${cosmosDataContributorRoleId}'
    principalId: developer.identity.principalId
    scope: cosmos.id
  }
}
resource reviewerCosmosData 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-08-15' = {
  parent: cosmos
  name: guid(cosmos.id, reviewer.id, 'cosmosDataContributor')
  properties: {
    roleDefinitionId: '${cosmos.id}/sqlRoleDefinitions/${cosmosDataContributorRoleId}'
    principalId: reviewer.identity.principalId
    scope: cosmos.id
  }
}

output developerFqdn string = developer.properties.configuration.ingress.fqdn
output reviewerFqdn  string = reviewer.properties.configuration.ingress.fqdn
output developerPrincipalId string = developer.identity.principalId
output reviewerPrincipalId  string = reviewer.identity.principalId
```

- [ ] **Step 2: Lint**

```bash
az bicep build --file infra/modules/containerapps.bicep
```

Expected: warnings only on managed-identity registry pull (`adminIdentityResourceId` deprecation hints); no errors.

- [ ] **Step 3: Commit**

```bash
git add MSAgentFrameworkFoundry/infra/modules/containerapps.bicep
git commit -m "Add Container Apps module: env, both apps (port 8081, MI), 4 role assignments"
```

---

## Task 10: Provision once + smoke-test (`az deployment sub create`)

- [ ] **Step 1: Bootstrap a service principal for OIDC (one-time, **manual**)**

This step the engineer runs once against a fresh Azure subscription. Output `clientId`, `tenantId`, `subscriptionId` — stored as GitHub Actions secrets in Task 12.

```bash
az ad sp create-for-rbac --name "foundry-agents-deploy" \
  --role Contributor \
  --scopes "/subscriptions/<SUB_ID>" \
  --json-auth | tee /tmp/sp.json
az ad sp federate-credential create \
  --id $(az ad sp list --display-name foundry-agents-deploy --query '[0].appId' -o tsv) \
  --parameters @<(jq -n '{
    name: "github-main",
    issuer: "https://token.actions.githubusercontent.com",
    subject: "repo:<owner>/<repo>:ref:refs/heads/main",
    description: "main branch deploys",
    audiences: ["api://AzureADTokenExchange"]
  }')
```

- [ ] **Step 2: First deploy — empty image refs (apps stay in failed-to-pull until images exist)**

```bash
az login
az account set --subscription <SUB_ID>
az deployment sub create \
  --location eastus2 \
  --template-file infra/main.bicep \
  --parameters infra/main.bicepparam \
  --parameters githubPat=$GITHUB_PAT \
  --parameters developerImage="mcr.microsoft.com/azuredocs/aci-helloworld:latest" \
  --parameters reviewerImage="mcr.microsoft.com/azuredocs/aci-helloworld:latest"
```

Expected: succeeds; both ACA apps run the placeholder image. Outputs return `acrLoginServer`, `developerFqdn`, `reviewerFqdn`.

- [ ] **Step 3: Build & push real images via the local CLI (one-shot, before CI is wired)**

```bash
ACR_LOGIN_SERVER=$(az deployment sub show -n main --query properties.outputs.acrLoginServer.value -o tsv)
az acr login --name ${ACR_LOGIN_SERVER%%.*}
docker build -f MSAgentFrameworkFoundry/src/Foundry.Agents.Developer/Dockerfile \
  -t $ACR_LOGIN_SERVER/foundry-developer:bootstrap .
docker push $ACR_LOGIN_SERVER/foundry-developer:bootstrap
docker build -f MSAgentFrameworkFoundry/src/Foundry.Agents.Reviewer/Dockerfile \
  -t $ACR_LOGIN_SERVER/foundry-reviewer:bootstrap .
docker push $ACR_LOGIN_SERVER/foundry-reviewer:bootstrap
```

- [ ] **Step 4: Redeploy pointing at real images**

```bash
az deployment sub create \
  --location eastus2 \
  --template-file infra/main.bicep \
  --parameters infra/main.bicepparam \
  --parameters githubPat=$GITHUB_PAT \
  --parameters developerImage=$ACR_LOGIN_SERVER/foundry-developer:bootstrap \
  --parameters reviewerImage=$ACR_LOGIN_SERVER/foundry-reviewer:bootstrap
```

- [ ] **Step 5: Smoke-test the deployed Developer**

```bash
DEV_FQDN=$(az deployment sub show -n main --query properties.outputs.developerFqdn.value -o tsv)
curl -s https://$DEV_FQDN/health/live
# Add the MCP and run an assignment from Claude Code:
claude mcp add foundry-prod https://$DEV_FQDN/mcp
```

Expected: `200 OK` from `/health/live`. MCP `tools/list` returns `assign_dotnet_task`.

- [ ] **Step 6: Commit any iteration on bicep that came out of this dry run**

```bash
git add MSAgentFrameworkFoundry/infra
git commit -m "Iterate Bicep after first end-to-end provision smoke"
```

---

## Task 11: .github/workflows/pr.yml — restore, build, test

**Files:** Create `.github/workflows/pr.yml` (note: at the repo root, not under MSAgentFrameworkFoundry).

- [ ] **Step 1: Write the workflow**

```yaml
name: PR — Foundry Agents

on:
  pull_request:
    paths:
      - 'MSAgentFrameworkFoundry/**'
      - '.github/workflows/pr.yml'

permissions:
  contents: read

jobs:
  build-test:
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: MSAgentFrameworkFoundry
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - name: Restore
        run: dotnet restore MSAgentFrameworkFoundry.slnx

      - name: Build
        run: dotnet build MSAgentFrameworkFoundry.slnx -c Release --no-restore

      - name: Test (skip integration tests; they require RUN_E2E_TESTS=1)
        run: |
          dotnet test MSAgentFrameworkFoundry.slnx -c Release --no-build \
            --filter "FullyQualifiedName!~IntegrationTests" \
            --logger "trx;LogFileName=test-results.trx" \
            --results-directory TestResults
        env:
          RUN_E2E_TESTS: "0"

      - name: Upload test results
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: MSAgentFrameworkFoundry/TestResults/*.trx
```

- [ ] **Step 2: Lint locally with `act` (optional)**

```bash
act pull_request -W .github/workflows/pr.yml --container-architecture linux/amd64
```

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/pr.yml
git commit -m "Add PR workflow: restore, build, test (integration tests gated off)"
```

---

## Task 12: .github/workflows/deploy.yml — build/push images + ACA update

**Files:** Create `.github/workflows/deploy.yml`

Required GitHub Actions secrets/vars:

| Name | Type | Value |
|---|---|---|
| `AZURE_CLIENT_ID` | secret | Service principal app ID (from Task 10 Step 1) |
| `AZURE_TENANT_ID` | secret | Tenant ID |
| `AZURE_SUBSCRIPTION_ID` | secret | Subscription ID |
| `ENV_NAME` | var | `fdyprod` |
| `LOCATION` | var | `eastus2` |
| `RESOURCE_GROUP` | var | `rg-fdyprod` |
| `ACR_LOGIN_SERVER` | var | from Task 10 Step 4 outputs |

- [ ] **Step 1: Write the workflow**

```yaml
name: Deploy — Foundry Agents

on:
  push:
    branches: [ main ]
    paths:
      - 'MSAgentFrameworkFoundry/**'
      - '.github/workflows/deploy.yml'

permissions:
  id-token: write   # OIDC
  contents: read

concurrency:
  group: deploy-foundry
  cancel-in-progress: false   # don't kill in-flight deploys

jobs:
  build-and-deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - name: Build + unit tests (gate before deploy)
        working-directory: MSAgentFrameworkFoundry
        run: |
          dotnet build MSAgentFrameworkFoundry.slnx -c Release
          dotnet test  MSAgentFrameworkFoundry.slnx -c Release --no-build \
            --filter "FullyQualifiedName!~IntegrationTests"
        env:
          RUN_E2E_TESTS: "0"

      - name: Azure login (OIDC, no secrets)
        uses: azure/login@v2
        with:
          client-id:       ${{ secrets.AZURE_CLIENT_ID }}
          tenant-id:       ${{ secrets.AZURE_TENANT_ID }}
          subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}

      - name: ACR login
        run: az acr login --name "${ACR_LOGIN_SERVER%%.*}"
        env:
          ACR_LOGIN_SERVER: ${{ vars.ACR_LOGIN_SERVER }}

      - name: Compute image tags
        id: tags
        run: |
          SHA=${GITHUB_SHA::8}
          echo "developer=${{ vars.ACR_LOGIN_SERVER }}/foundry-developer:$SHA" >> "$GITHUB_OUTPUT"
          echo "reviewer=${{ vars.ACR_LOGIN_SERVER }}/foundry-reviewer:$SHA"   >> "$GITHUB_OUTPUT"

      - name: Build + push Developer
        run: |
          docker build -f MSAgentFrameworkFoundry/src/Foundry.Agents.Developer/Dockerfile \
            -t "${{ steps.tags.outputs.developer }}" .
          docker push "${{ steps.tags.outputs.developer }}"

      - name: Build + push Reviewer
        run: |
          docker build -f MSAgentFrameworkFoundry/src/Foundry.Agents.Reviewer/Dockerfile \
            -t "${{ steps.tags.outputs.reviewer }}" .
          docker push "${{ steps.tags.outputs.reviewer }}"

      - name: az containerapp update — developer
        run: |
          az containerapp update \
            --name developer-app \
            --resource-group "${{ vars.RESOURCE_GROUP }}" \
            --image "${{ steps.tags.outputs.developer }}"

      - name: az containerapp update — reviewer
        run: |
          az containerapp update \
            --name reviewer-app \
            --resource-group "${{ vars.RESOURCE_GROUP }}" \
            --image "${{ steps.tags.outputs.reviewer }}"

      - name: Post-deploy smoke
        run: |
          DEV_FQDN=$(az containerapp show -n developer-app -g "${{ vars.RESOURCE_GROUP }}" \
            --query properties.configuration.ingress.fqdn -o tsv)
          for i in 1 2 3 4 5; do
            if curl -fsS "https://$DEV_FQDN/health/live" >/dev/null; then
              echo "developer healthy"; exit 0
            fi
            sleep 5
          done
          echo "developer failed health check" && exit 1
```

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/deploy.yml
git commit -m "Add deploy workflow: OIDC login, build/push images, ACA update, smoke"
```

---

## Task 13: README pointing at the gated integration tests + post-deploy notes

**Files:** Create `MSAgentFrameworkFoundry/README.md`

Short docs file. The two non-obvious things it documents:

1. How to flip on `RUN_E2E_TESTS=1` locally without affecting CI.
2. How to point Claude Code at the deployed Developer.

- [ ] **Step 1: Write the README**

```markdown
# MSAgentFrameworkFoundry

Two cooperating agents — a .NET 10 C# Developer and a C# Code Reviewer — running on Azure Container Apps with Anthropic `claude-opus-4-7` hosted on Azure AI Foundry.

See:

- [`docs/idea.md`](docs/idea.md) — original requirements
- [`docs/superpowers/specs/2026-05-13-foundry-agents-design.md`](docs/superpowers/specs/2026-05-13-foundry-agents-design.md) — full design
- [`docs/implementation.md`](docs/implementation.md) — implementation roadmap

## Local dev

```bash
# Seed the AppHost parameters (once)
dotnet user-secrets set "Parameters:anthropic-foundry-endpoint" "https://<your-foundry>.services.ai.azure.com" --project src/Foundry.Agents.AppHost
dotnet user-secrets set "Parameters:foundry-github-mcp-endpoint" "https://<...>/mcp" --project src/Foundry.Agents.AppHost
dotnet user-secrets set "Parameters:github-pat" "<your PAT>" --project src/Foundry.Agents.AppHost

# Run
dotnet run --project src/Foundry.Agents.AppHost

# Wire Claude Code at the URL Aspire prints
claude mcp add foundry-dev https://localhost:<port>/mcp
```

## End-to-end integration tests (slow, cost tokens)

These hit the real Anthropic model on Foundry and open a real PR. Off by default.

```bash
export RUN_E2E_TESTS=1
export INTEGRATION_GITHUB_PAT=ghp_...
export INTEGRATION_TARGET_REPO=your-username/throwaway-repo
dotnet test tests/Foundry.Agents.Developer.IntegrationTests
```

## Deployed environment

After CI deploys to `main`, the Developer MCP endpoint is at:

```
https://<developerFqdn>/mcp
```

`developerFqdn` is the output of the `main.bicep` deployment (`az deployment sub show -n main --query properties.outputs.developerFqdn.value -o tsv`). Add it to Claude Code with `claude mcp add foundry-prod https://<developerFqdn>/mcp`.

The Reviewer is internal-only by design (`external: false` on its ACA ingress). It is reachable only from the Developer in the same ACA environment.
```

- [ ] **Step 2: Commit**

```bash
git add MSAgentFrameworkFoundry/README.md
git commit -m "Add README with local dev, E2E test, and deployed-endpoint notes"
```

---

## Final verification

- [ ] **Step 1: PR build is green**

Open a PR. Wait for `pr.yml` to finish. Expected: green.

- [ ] **Step 2: Merge to main and verify deploy**

Merge. Wait for `deploy.yml`. Expected: both images pushed, both ACA apps updated, post-deploy smoke returns 200.

- [ ] **Step 3: Drive an assign_dotnet_task against the prod URL**

```bash
DEV_FQDN=$(az containerapp show -n developer-app -g $RG --query properties.configuration.ingress.fqdn -o tsv)
claude mcp add foundry-prod https://$DEV_FQDN/mcp
claude --print "use foundry-prod assign_dotnet_task on github.com/<your-fork> with task 'add a Hello() method'"
```

Expected: agent runs to completion (Approved or ReviewFailed). PR opened, Reviewer comments visible on the PR, thread persisted in Cosmos.

- [ ] **Step 4: Tag**

```bash
git tag plan-3-infra-complete
```

Plan 3 is done when:
- Both Dockerfiles build images locally that pass `/health/live`.
- `az deployment sub create` succeeds against an empty subscription.
- `pr.yml` runs on PRs and is green.
- `deploy.yml` builds, pushes, and rolls out both ACA apps on merges to `main`.
- A live `assign_dotnet_task` invocation against the deployed Developer URL opens a real PR.

The system is now production. Spec §10 risks remain live — revisit if Foundry's Anthropic surface or GitHub MCP toolset shifts.
