# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.

# Runtime smoke validation for an installed IIS Administration service. Run on a Windows machine
# (or CI runner) where the service is installed and the current user is an API administrator.
# Covers the fork's regression fixes:
#  - never-expiring access keys authenticate at a negative UTC offset (issues #329/#331)
#  - concurrent applicationHost.config commits return 409, never an unhandled 500 (issue #324)
#  - plugin loading, HTTP.sys Windows authentication, and the access-keys UI respond
#
# Uses System.Net.Http.HttpClient directly (the same stack as the integration suite's
# ApiHttpClient): PowerShell's Invoke-WebRequest does not complete the Negotiate/NTLM
# handshake against this service.

#Requires -Version 7
[CmdletBinding()]
param(
    [string] $ServerUrl = "https://localhost:55539",
    [int] $StartupTimeoutSeconds = 120,
    [int] $ConcurrentPatches = 8,
    # Explicit credentials for Windows auth. Default-credential SSO falls back to an
    # anonymous NTLM logon in GitHub-hosted runner sessions, so CI passes a dedicated
    # local account; interactive/manual runs can omit these to use the current user.
    [string] $UserName,
    [string] $Password
)

$ErrorActionPreference = 'Stop'
$failures = [System.Collections.Generic.List[string]]::new()

function Step($name, [scriptblock] $body) {
    Write-Host "==> $name"
    try {
        & $body
        Write-Host "    PASS"
    } catch {
        Write-Host "    FAIL: $_"
        $failures.Add("${name}: $_")
    }
}

function New-WindowsAuthClient {
    $handler = [System.Net.Http.HttpClientHandler]::new()
    if ($UserName) {
        $handler.Credentials = [System.Net.NetworkCredential]::new($UserName, $Password)
    } else {
        $handler.UseDefaultCredentials = $true
    }
    $handler.ServerCertificateCustomValidationCallback = [System.Net.Http.HttpClientHandler]::DangerousAcceptAnyServerCertificateValidator
    [System.Net.Http.HttpClient]::new($handler)
}

function New-TokenClient([string] $accessToken) {
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.ServerCertificateCustomValidationCallback = [System.Net.Http.HttpClientHandler]::DangerousAcceptAnyServerCertificateValidator
    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.DefaultRequestHeaders.Add('Access-Token', "Bearer $accessToken")
    $client.DefaultRequestHeaders.Add('Accept', 'application/hal+json')
    $client
}

$winClient = New-WindowsAuthClient

# --- Wait for the service to answer ---
$deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
$alive = $false
$probeErrors = @{}
while ((Get-Date) -lt $deadline) {
    try {
        $r = $winClient.GetAsync("$ServerUrl/security/api-keys").GetAwaiter().GetResult()
        if ($r.StatusCode -eq 200) { $alive = $true; $r.Dispose(); break }
        $reason = "HTTP $([int]$r.StatusCode)"
        $r.Dispose()
    } catch {
        $reason = $_.Exception.Message
    }
    if (-not $probeErrors.ContainsKey($reason)) {
        $probeErrors[$reason] = 0
        Write-Host "    probe: $reason"
    }
    $probeErrors[$reason]++
    Start-Sleep -Seconds 3
}
if (-not $alive) {
    $summary = ($probeErrors.GetEnumerator() | ForEach-Object { "$($_.Value)x $($_.Key)" }) -join '; '
    Write-Error "Service did not answer at $ServerUrl within $StartupTimeoutSeconds seconds. Probe failures: $summary"
}
Write-Host "Service is up at $ServerUrl (timezone: $((Get-TimeZone).Id), UTC offset: $((Get-TimeZone).BaseUtcOffset))"

function Get-XsrfToken {
    $r = $winClient.GetAsync("$ServerUrl/security/api-keys").GetAwaiter().GetResult()
    try {
        $values = $null
        if (-not $r.Headers.TryGetValues('XSRF-TOKEN', [ref] $values)) {
            throw "no XSRF-TOKEN header on GET /security/api-keys (HTTP $([int]$r.StatusCode))"
        }
        $values | Select-Object -First 1
    } finally {
        $r.Dispose()
    }
}

# --- Acquire a NEVER-EXPIRING access key (issue #329/#331 regression: this token must work) ---
$token = $null
$keyId = $null
Step "Create never-expiring access key via Windows auth + XSRF" {
    $xsrf = Get-XsrfToken

    # expires_on must be present; an empty string means the key never expires
    $content = [System.Net.Http.StringContent]::new('{"purpose":"CI validation","expires_on":""}', [System.Text.Encoding]::UTF8, 'application/json')
    $content.Headers.Add('XSRF-TOKEN', $xsrf)
    $r = $winClient.PostAsync("$ServerUrl/security/api-keys", $content).GetAwaiter().GetResult()
    $body = $r.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    if ([int]$r.StatusCode -ge 300) { throw "HTTP $([int]$r.StatusCode): $body" }
    $r.Dispose()

    $key = $body | ConvertFrom-Json
    if (-not $key.access_token) { throw "no access_token in response: $body" }
    if ($key.expires_on) { throw "expected a never-expiring key, got expires_on=$($key.expires_on)" }
    $script:token = $key.access_token
    $script:keyId = $key.id
}

if (-not $token) {
    Write-Error "Cannot continue without an access token. Failures: $($failures -join '; ')"
}
$apiClient = New-TokenClient $token

Step "Never-expiring token authenticates (issue #329/#331)" {
    $r = $apiClient.GetAsync("$ServerUrl/api/webserver/application-pools").GetAwaiter().GetResult()
    if ([int]$r.StatusCode -ne 200) { throw "expected 200, got $([int]$r.StatusCode)" }
    $r.Dispose()
}

Step "Plugins loaded (webserver endpoints respond)" {
    foreach ($endpoint in 'application-pools', 'websites') {
        $r = $apiClient.GetAsync("$ServerUrl/api/webserver/$endpoint").GetAwaiter().GetResult()
        if ([int]$r.StatusCode -ne 200) { throw "GET /api/webserver/${endpoint}: expected 200, got $([int]$r.StatusCode)" }
        $r.Dispose()
    }
}

Step "Access-keys UI page renders" {
    $r = $winClient.GetAsync("$ServerUrl/security/tokens").GetAwaiter().GetResult()
    if ([int]$r.StatusCode -ne 200) { throw "expected 200, got $([int]$r.StatusCode)" }
    $r.Dispose()
}

Step "Concurrent app-pool PATCHes return only 200/409, no 500 (issue #324)" {
    $r = $apiClient.GetAsync("$ServerUrl/api/webserver/application-pools").GetAwaiter().GetResult()
    $pools = ($r.Content.ReadAsStringAsync().GetAwaiter().GetResult()) | ConvertFrom-Json
    $r.Dispose()
    $pool = $pools.app_pools | Select-Object -First 1
    if (-not $pool) { throw "no application pools found - is IIS installed?" }
    $poolUrl = "$ServerUrl/api/webserver/application-pools/$($pool.id)"

    $statuses = 1..$ConcurrentPatches | ForEach-Object -Parallel {
        $handler = [System.Net.Http.HttpClientHandler]::new()
        $handler.ServerCertificateCustomValidationCallback = [System.Net.Http.HttpClientHandler]::DangerousAcceptAnyServerCertificateValidator
        $client = [System.Net.Http.HttpClient]::new($handler)
        try {
            $request = [System.Net.Http.HttpRequestMessage]::new('PATCH', $using:poolUrl)
            $request.Headers.Add('Access-Token', "Bearer $using:token")
            $request.Content = [System.Net.Http.StringContent]::new(
                ('{"queue_length":' + (1000 + $_) + '}'), [System.Text.Encoding]::UTF8, 'application/json')
            $r = $client.SendAsync($request).GetAwaiter().GetResult()
            [int]$r.StatusCode
            $r.Dispose()
        } catch {
            -1
        } finally {
            $client.Dispose()
        }
    } -ThrottleLimit $ConcurrentPatches

    Write-Host "    statuses: $($statuses -join ', ')"
    $bad = $statuses | Where-Object { $_ -notin 200, 409 }
    if ($bad) { throw "unexpected status codes: $($bad -join ', ')" }
    if (-not ($statuses -contains 200)) { throw "no PATCH succeeded" }
}

# --- Cleanup the CI access key ---
if ($keyId) {
    try {
        $xsrf = Get-XsrfToken
        $request = [System.Net.Http.HttpRequestMessage]::new('DELETE', "$ServerUrl/security/api-keys/$keyId")
        $request.Headers.Add('XSRF-TOKEN', $xsrf)
        $winClient.SendAsync($request).GetAwaiter().GetResult().Dispose()
    } catch {
        Write-Host "cleanup: could not delete CI access key: $_"
    }
}

if ($failures.Count -gt 0) {
    Write-Error ("Smoke validation failed:`n - " + ($failures -join "`n - "))
}
Write-Host "All smoke validations passed."
