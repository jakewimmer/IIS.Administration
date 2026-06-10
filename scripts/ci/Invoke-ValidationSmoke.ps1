# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.

# Runtime smoke validation for an installed IIS Administration service. Run on a Windows machine
# (or CI runner) where the service is installed and the current user is an API administrator.
# Covers the fork's regression fixes:
#  - never-expiring access keys authenticate at a negative UTC offset (issues #329/#331)
#  - concurrent applicationHost.config commits return 409, never an unhandled 500 (issue #324)
#  - plugin loading, HTTP.sys Windows authentication, and the access-keys UI respond

#Requires -Version 7
[CmdletBinding()]
param(
    [string] $ServerUrl = "https://localhost:55539",
    [int] $StartupTimeoutSeconds = 120,
    [int] $ConcurrentPatches = 8
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

# --- Wait for the service to answer ---
$session = $null
$deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
$alive = $false
$probeErrors = @{}
while ((Get-Date) -lt $deadline) {
    try {
        $r = Invoke-WebRequest -Uri "$ServerUrl/security/api-keys" -UseDefaultCredentials -SkipCertificateCheck `
                               -SessionVariable session -Headers @{ Accept = 'application/hal+json' }
        if ($r.StatusCode -eq 200) { $alive = $true; break }
    } catch {
        $reason = if ($_.Exception.Response) {
            "HTTP $([int]$_.Exception.Response.StatusCode) from $($_.Exception.Response.RequestMessage.RequestUri)"
        } else {
            $_.Exception.Message
        }
        if (-not $probeErrors.ContainsKey($reason)) {
            $probeErrors[$reason] = 0
            Write-Host "    probe: $reason"
        }
        $probeErrors[$reason]++
        Start-Sleep -Seconds 3
    }
}
if (-not $alive) {
    $summary = ($probeErrors.GetEnumerator() | ForEach-Object { "$($_.Value)x $($_.Key)" }) -join '; '
    Write-Error "Service did not answer at $ServerUrl within $StartupTimeoutSeconds seconds. Probe failures: $summary"
}
Write-Host "Service is up at $ServerUrl (timezone: $((Get-TimeZone).Id), UTC offset: $((Get-TimeZone).BaseUtcOffset))"

# --- Acquire a NEVER-EXPIRING access key (issue #329/#331 regression: this token must work) ---
$token = $null
$keyId = $null
Step "Create never-expiring access key via NTLM + XSRF" {
    $r = Invoke-WebRequest -Uri "$ServerUrl/security/api-keys" -UseDefaultCredentials -SkipCertificateCheck -WebSession $session
    $xsrf = $r.Headers['XSRF-TOKEN'] | Select-Object -First 1
    if (-not $xsrf) { throw "no XSRF-TOKEN header on GET /security/api-keys" }

    # No expires_on -> the key never expires
    $r = Invoke-WebRequest -Uri "$ServerUrl/security/api-keys" -Method Post -UseDefaultCredentials -SkipCertificateCheck `
                           -WebSession $session -ContentType 'application/json' `
                           -Headers @{ 'XSRF-TOKEN' = $xsrf } -Body (@{ purpose = 'CI validation' } | ConvertTo-Json)
    $key = $r.Content | ConvertFrom-Json
    if (-not $key.access_token) { throw "no access_token in response: $($r.Content)" }
    if ($key.expires_on) { throw "expected a never-expiring key, got expires_on=$($key.expires_on)" }
    $script:token = $key.access_token
    $script:keyId = $key.id
}

if (-not $token) {
    Write-Error "Cannot continue without an access token. Failures: $($failures -join '; ')"
}
$authHeaders = @{ 'Access-Token' = "Bearer $token"; Accept = 'application/hal+json' }

Step "Never-expiring token authenticates (issue #329/#331)" {
    $r = Invoke-WebRequest -Uri "$ServerUrl/api/webserver/application-pools" -SkipCertificateCheck -Headers $authHeaders
    if ($r.StatusCode -ne 200) { throw "expected 200, got $($r.StatusCode)" }
}

Step "Plugins loaded (webserver endpoints respond)" {
    foreach ($endpoint in 'application-pools', 'websites') {
        $r = Invoke-WebRequest -Uri "$ServerUrl/api/webserver/$endpoint" -SkipCertificateCheck -Headers $authHeaders
        if ($r.StatusCode -ne 200) { throw "GET /api/webserver/${endpoint}: expected 200, got $($r.StatusCode)" }
    }
}

Step "Access-keys UI page renders" {
    $r = Invoke-WebRequest -Uri "$ServerUrl/security/tokens" -UseDefaultCredentials -SkipCertificateCheck -WebSession $session
    if ($r.StatusCode -ne 200) { throw "expected 200, got $($r.StatusCode)" }
}

Step "Concurrent app-pool PATCHes return only 200/409, no 500 (issue #324)" {
    $pools = (Invoke-WebRequest -Uri "$ServerUrl/api/webserver/application-pools" -SkipCertificateCheck -Headers $authHeaders).Content | ConvertFrom-Json
    $pool = $pools.app_pools | Select-Object -First 1
    if (-not $pool) { throw "no application pools found - is IIS installed?" }
    $poolUrl = "$ServerUrl/api/webserver/application-pools/$($pool.id)"

    $statuses = 1..$ConcurrentPatches | ForEach-Object -Parallel {
        try {
            $r = Invoke-WebRequest -Uri $using:poolUrl -Method Patch -SkipCertificateCheck `
                                   -Headers $using:authHeaders -ContentType 'application/json' `
                                   -Body (@{ queue_length = 1000 + $_ } | ConvertTo-Json) -SkipHttpErrorCheck
            [int]$r.StatusCode
        } catch {
            -1
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
        $r = Invoke-WebRequest -Uri "$ServerUrl/security/api-keys" -UseDefaultCredentials -SkipCertificateCheck -WebSession $session
        $xsrf = $r.Headers['XSRF-TOKEN'] | Select-Object -First 1
        Invoke-WebRequest -Uri "$ServerUrl/security/api-keys/$keyId" -Method Delete -UseDefaultCredentials `
                          -SkipCertificateCheck -WebSession $session -Headers @{ 'XSRF-TOKEN' = $xsrf } | Out-Null
    } catch {
        Write-Host "cleanup: could not delete CI access key: $_"
    }
}

if ($failures.Count -gt 0) {
    Write-Error ("Smoke validation failed:`n - " + ($failures -join "`n - "))
}
Write-Host "All smoke validations passed."
