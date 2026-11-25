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
        return $row.StringData(1)
    }
    finally { $view.Close() }
}

$actual = Read-MsiProperty 'ProductVersion'
if ($actual -ne $ExpectedVersion) {
    throw "Expected MSI version $ExpectedVersion; got $actual"
}
$name = Read-MsiProperty 'ProductName'
if ($name -ne 'Stream Display Manager') { throw "Unexpected product: $name" }
$upgrade = Read-MsiProperty 'UpgradeCode'
if ($upgrade -ne '{3AA12A9B-01AE-47BC-AC6E-02E1BB671583}') {
    throw "Unexpected UpgradeCode: $upgrade"
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
