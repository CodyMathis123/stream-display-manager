param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$parts = @($Version.Split('.') | ForEach-Object { [int]$_ })
if ($parts.Count -ne 3 -or @($parts | Where-Object { $_ -gt 65535 -or $_ -lt 0 }).Count -gt 0) {
    throw 'Windows VERSIONINFO fields must be between 0 and 65535.'
}
$major, $minor, $build = $parts
$full = "$major.$minor.$build.0"
$header = @"
#pragma once
// Generated from the same APP_VERSION used by the MSI; do not edit.
#define SDM_VERSION_MAJOR $major
#define SDM_VERSION_MINOR $minor
#define SDM_VERSION_BUILD $build
#define SDM_VERSION_REVISION 0
#define SDM_VERSION_TEXT `"$full\0`"
"@
$parent = Split-Path -Parent $OutputPath
if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
[System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($OutputPath), $header + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
Write-Host "Generated Windows VERSIONINFO for $full"
