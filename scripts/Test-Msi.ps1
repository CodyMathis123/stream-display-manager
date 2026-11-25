param(
    [Parameter(Mandatory = $true)][string]$MsiPath,
    [Parameter(Mandatory = $true)][string]$ExpectedVersion
)
$ErrorActionPreference = 'Stop'
$fullPath = (Resolve-Path -LiteralPath $MsiPath).Path
if ((Get-Item $fullPath).Length -lt 10240) {
    throw "MSI appears truncated: $fullPath"
}

$installer = New-Object -ComObject WindowsInstaller.Installer
$db = $installer.OpenDatabase($fullPath, 0)

function Read-MsiProperty([string]$Name) {
    $view = $db.OpenView("SELECT Value FROM Property WHERE Property='$Name'")
    try {
        $view.Execute()
        $row = $view.Fetch()
        if ($null -eq $row) { return $null }
        return [string]($row.StringData(1))
    }
    finally { $view.Close() }
}

# Windows Installer COM sometimes exposes padded values. Parse the precise
# three-part numeric ProductVersion and compare it to CI's expected value.
$rawVersion = [string](Read-MsiProperty 'ProductVersion')
$versionMatch = [regex]::Match($rawVersion, '(?<!\d)\d+\.\d+\.\d+(?!\d)')
$actual = if ($versionMatch.Success) { $versionMatch.Value } else { '' }
if ($actual -ne $ExpectedVersion) {
    throw "Expected MSI version $ExpectedVersion; got raw '$rawVersion'"
}

$name = [string](Read-MsiProperty 'ProductName')
if ($name -notmatch '^\s*Stream Display Manager\s*$') {
    throw "Unexpected ProductName: '$name'"
}

$upgradeRaw = [string](Read-MsiProperty 'UpgradeCode')
$upgrade = [regex]::Match(
    $upgradeRaw,
    '[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}'
).Value.ToUpperInvariant()
if ($upgrade -ne '3AA12A9B-01AE-47BC-AC6E-02E1BB671583') {
    throw "Unexpected UpgradeCode: '$upgradeRaw'"
}

$allUsers = Read-MsiProperty 'ALLUSERS'
if ($allUsers -eq '1') {
    throw 'Per-user MSI accidentally became per-machine'
}

$view = $db.OpenView("SELECT FileName FROM File")
try {
    $view.Execute()
    $names = [System.Collections.Generic.List[string]]::new()
    while ($row = $view.Fetch()) {
        $names.Add([string]($row.StringData(1)))
    }
    if (-not ($names | Where-Object { $_ -like '*StreamDisplayManager.App.exe*' })) {
        throw 'Published executable is missing from MSI File table'
    }
}
finally { $view.Close() }

Write-Host "Validated MSI: $name $actual, per-user, stable upgrade identity."
