# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.

# Verifies that the <RemotePayload> metadata in the installer bundle matches the real runtime
# installers it points at. Downloads each ExePackage's DownloadUrl, regenerates the payload
# metadata with WiX heat.exe, and fails (printing the correct XML) if the committed values drift.

#Requires -Version 7
[CmdletBinding()]
param(
    [string] $BundleWxs = "installer/IISAdministrationBundle/iisadministration.wxs",
    [string] $HeatExe = "installer/packages/WiX.3.14.1/tools/heat.exe"
)

$ErrorActionPreference = 'Stop'
$compared = @('CertificatePublicKey', 'CertificateThumbprint', 'Hash', 'Size', 'Version')

if (-not (Test-Path $HeatExe)) { Write-Error "heat.exe not found at $HeatExe - run nuget restore for the installer first" }

[xml]$bundle = Get-Content $BundleWxs
$packages = $bundle.Wix.Fragment.PackageGroup.ExePackage | Where-Object { $_ }

$drift = $false
foreach ($pkg in $packages) {
    $name = $pkg.Name
    $url = $pkg.DownloadUrl
    Write-Host "==> $name"
    Write-Host "    downloading $url"

    $exePath = Join-Path ([System.IO.Path]::GetTempPath()) $name
    Invoke-WebRequest -Uri $url -OutFile $exePath

    $genWxs = "$exePath.wxs"
    & $HeatExe payload $exePath -o $genWxs | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Error "heat.exe failed for $name" }

    # Locate the harvested payload element regardless of heat's exact output shape
    [xml]$gen = Get-Content $genWxs
    $generated = $gen.SelectSingleNode('//*[@Hash]')
    if (-not $generated) {
        Write-Host "    heat.exe output did not contain a payload element with a Hash attribute:"
        Get-Content $genWxs | ForEach-Object { Write-Host "    $_" }
        Write-Error "Unrecognized heat.exe output for $name"
    }

    foreach ($attr in $compared) {
        $expected = $generated.GetAttribute($attr)
        $actual = $pkg.RemotePayload.$attr
        if ($expected -ne $actual) {
            Write-Host "    DRIFT ${attr}: committed '$actual' != actual '$expected'"
            $drift = $true
        }
    }

    if ($env:GITHUB_STEP_SUMMARY) {
        @("### $name", '', '```xml', $generated.OuterXml, '```', '') | Add-Content $env:GITHUB_STEP_SUMMARY
    }
}

if ($drift) {
    Write-Error "RemotePayload metadata is stale. Update $BundleWxs with the values printed above (also in the job summary)."
}
Write-Host "All RemotePayload metadata matches the published installers."
