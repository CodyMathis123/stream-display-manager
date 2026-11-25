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
        return ($row.StringData(1)).Trim()
    }
    finally { $view.Close() }
}

$rawVersion = [string](Read-MsiProperty 'ProductVersion')
# Windows Installer COM may return padded values. Extract the three-part
# numeric MSI version and still require an exact match against CI's version.
$versionMatch = [regex]::Match($rawVersion, '(?<!\d)\d+\.\d+\.\d+(?!\d)')
$actual = if ($versionMatch.Success) { $versionMatch.Value } else { '' }
if ($actual -ne $ExpectedVersion) {
    throw "Expected MSI version $ExpectedVersion; got raw '$rawVersion'"
}
$name = [string](Read-MsiProperty 'ProductName')
if ($name -notmatch '^\s*Stream Display Manager\s*
$allUsers = Read-MsiProperty 'ALLUSERS'
if ($allUsers -eq '1') { throw 'Per-user MSI accidentally became per-machine' }

$view = $db.OpenView("SELECT FileName FROM File")
try {
    $view.Execute()
    $names = [System.Collections.Generic.List[string]]::new()
    while ($row = $view.Fetch()) { $names.Add($row.StringData(1)) }
    if (-not ($names | Where-Object { $_ -like '*StreamDisplayManager.App.exe*' })) {
        throw 'Published EXE is absent from MSI File table'
    }
}
finally { $view.Close() }

Write-Host "Validated MSI: $name $actual, per-user, stable upgrade identity."
) {
    throw "Unexpected product: $name"
}
$upgradeRaw = [string](Read-MsiProperty 'UpgradeCode')
$upgrade = [regex]::Match(
    $upgradeRaw,
    '[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}'
).Value.ToUpperInvariant()
if ($upgrade -ne '3AA12A9B-01AE-47BC-AC6E-02E1BB671583') {
    throw "Unexpected UpgradeCode: $upgradeRaw"
}
$allUsers = Read-MsiProperty 'ALLUSERS'
if ($allUsers -eq '1') { throw 'Per-user MSI accidentally became per-machine' }

$view = $db.OpenView("SELECT FileName FROM File")
try {
    $view.Execute()
    $names = [System.Collections.Generic.List[string]]::new()
    while ($row = $view.Fetch()) { $names.Add($row.StringData(1)) }
    if (-not ($names | Where-Object { $_ -like '*StreamDisplayManager.App.exe*' })) {
        throw 'Published EXE is absent from MSI File table'
    }
}
finally { $view.Close() }

Write-Host "Validated MSI: $name $actual, per-user, stable upgrade identity."
