# FM-11 environment readiness inspection (read-only; no tokens or secrets).
# Usage: powershell -NoProfile -File scripts/fm11_http_environment_probe.ps1
param(
  [string]$EnvironmentId = $env:FM_ENVIRONMENT_ID,
  [string]$TenantId = $env:FM_TENANT_ID
)
$ErrorActionPreference = 'Stop'
if (-not $EnvironmentId -or -not $TenantId) { throw 'Specify -EnvironmentId and -TenantId. No credential is required.' }
Write-Output 'FM11_HTTP_PROBE_READ_ONLY=true'
Write-Output 'FM11_PROBE_NEVER_CREATES_HTTP_TRIGGER=true'
$openidUrl = "https://login.microsoftonline.com/$TenantId/v2.0/.well-known/openid-configuration"
try {
  $openid = Invoke-RestMethod -Uri $openidUrl -Method Get -TimeoutSec 20
  $matchesTenant = $openid.issuer -like "*$TenantId/v2.0"
  $hasDeviceFlow = [bool]$openid.device_authorization_endpoint
  Write-Output "ENTRA_TENANT_DISCOVERY_VALID=$matchesTenant"
  Write-Output "ENTRA_DEVICE_CODE_ENDPOINT_PRESENT=$hasDeviceFlow"
} catch {
  Write-Output 'ENTRA_TENANT_DISCOVERY_VALID=unknown'
  Write-Output 'ENTRA_DEVICE_CODE_ENDPOINT_PRESENT=unknown'
}
try {
  $lines = (pac licensing get-many-environment-entitlements --environment $EnvironmentId 2>&1 | Out-String)
  $start = $lines.IndexOf('[')
  if ($start -lt 0) { throw 'Entitlements not returned as JSON' }
  $entries = ConvertFrom-Json -InputObject $lines.Substring($start)
  foreach ($id in @('PowerAutomatePerProcess', 'PerFlowPlan')) {
    $match = $null
    foreach ($item in $entries) {
      if ($item.entitlementId -eq $id) { $match = $item; break }
    }
    if ($null -ne $match) {
      Write-Output "$($id)_ALLOCATED=$($match.entitlement.capacity.allocated.value)"
      Write-Output "$($id)_AVAILABLE=$($match.entitlement.capacity.availableQuantity)"
    } else {
      Write-Output "$($id)_ALLOCATED=unknown"
    }
  }
  foreach ($item in $entries) {
    if ($item.environmentType) {
      Write-Output "ENVIRONMENT_TYPE=$($item.environmentType)"
      Write-Output "ENVIRONMENT_IS_MANAGED=$($item.isManagedEnvironment)"
      break
    }
  }
} catch {
  Write-Output 'ENTITLEMENTS_NOT_VERIFIED=true'
}
try {
  $inventory = (pac power-automate list-cloud-flows --environment $EnvironmentId --columns 'Name,WorkflowId' 2>&1 | Out-String)
  if ($inventory -match '(?im)^FM11\s') {
    Write-Output 'FM11_FLOW_FOUND=true'
  } elseif ($inventory -match 'WorkflowId') {
    Write-Output 'FM11_FLOW_FOUND=false'
  } else {
    Write-Output 'FM11_FLOW_FOUND=unknown'
  }
} catch {
  Write-Output 'FM11_FLOW_FOUND=unknown'
}
# Neither tenant OIDC discovery nor environment entitlements establish a
# licensed Premium user, an Entra app registration, delegated consent,
# or an authenticated iPhone invocation of a restricted HTTP trigger.
Write-Output 'USER_PREMIUM_LICENSE_VERIFIED=false'
Write-Output 'DELEGATED_IPHONE_AUTH_VERIFIED=false'
Write-Output 'HTTP_TRIGGER_DEPLOYMENT_APPROVED=false'
