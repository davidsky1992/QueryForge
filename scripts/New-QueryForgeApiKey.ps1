<#
.SYNOPSIS
    Generates a QueryForge API key and the SHA-256 hash to put in configuration.

.DESCRIPTION
    The key is shown once and is never written to disk by this script. Give the key to the client and store
    only the hash in configuration (user secrets, .env, or a secret store such as Azure Key Vault).

.EXAMPLE
    powershell -File scripts/New-QueryForgeApiKey.ps1 -Name local-admin -Role admin
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9._-]{1,64}$')]
    [string] $Name,

    [Parameter(Mandatory = $true)]
    [ValidateSet('admin', 'reader')]
    [string] $Role
)

$ErrorActionPreference = 'Stop'

# 32 bytes from a cryptographic RNG, base64url-encoded (43 characters).
$bytes = New-Object byte[] 32
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
$key = [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')

# Must match ApiKeyStore.ComputeHash: lowercase hex SHA-256 of the UTF-8 key.
$sha = [System.Security.Cryptography.SHA256]::Create()
try { $hashBytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($key)) } finally { $sha.Dispose() }
$hash = -join ($hashBytes | ForEach-Object { $_.ToString('x2') })

Write-Host ''
Write-Host "API key '$Name' (role: $Role)" -ForegroundColor Cyan
Write-Host ''
Write-Host 'Key (give to the client; shown only once, not stored anywhere):' -ForegroundColor Yellow
Write-Host "  $key"
Write-Host ''
Write-Host 'SHA-256 hash (put in configuration):' -ForegroundColor Yellow
Write-Host "  $hash"
Write-Host ''
Write-Host 'Configuration examples:' -ForegroundColor Yellow
Write-Host "  Docker Compose (.env):  QUERYFORGE_$($Role.ToUpperInvariant())_KEY_SHA256=$hash"
Write-Host '  User secrets (API on the host):'
Write-Host "    dotnet user-secrets set `"Security:ApiKeys:0:Name`" `"$Name`" --project src/QueryForge.Api"
Write-Host "    dotnet user-secrets set `"Security:ApiKeys:0:Hash`" `"$hash`" --project src/QueryForge.Api"
Write-Host "    dotnet user-secrets set `"Security:ApiKeys:0:Roles`" `"$Role`" --project src/QueryForge.Api"
Write-Host '  (Use a different index, e.g. :1:, for each additional key.)'
Write-Host ''
