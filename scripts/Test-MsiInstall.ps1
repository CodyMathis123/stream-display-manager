param([Parameter(Mandatory = $true)][string]$MsiPath)
$ErrorActionPreference = 'Stop'
$msi = (Resolve-Path -LiteralPath $MsiPath).Path
$msiexec = Join-Path $env:SystemRoot 'System32\msiexec.exe'
$program = Join-Path $env:LOCALAPPDATA 'StreamDisplayManager\StreamDisplayManager.App.exe'
$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Stream Display Manager.lnk'
$startupKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$installLog = Join-Path $env:RUNNER_TEMP 'stream-display-manager-install.log'
$uninstallLog = Join-Path $env:RUNNER_TEMP 'stream-display-manager-uninstall.log'

function Invoke-Msi([string]$Verb, [string]$LogPath) {
    $argsText = $Verb + ' "' + $msi + '" /qn /norestart /l*v "' + $LogPath + '"'
    $process = Start-Process -FilePath $msiexec -ArgumentList $argsText -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        Write-Host 'Last Windows Installer diagnostics:'
        if (Test-Path $LogPath) { Get-Content -LiteralPath $LogPath -Tail 40 }
        throw "msiexec $Verb returned $($process.ExitCode)"
    }
}

try {
    Invoke-Msi '/i' $installLog
    if (-not (Test-Path -LiteralPath $program)) {
        throw "Installed EXE missing from per-user folder: $program"
    }
    if (-not (Test-Path -LiteralPath $shortcut)) {
        throw "Start Menu shortcut not created: $shortcut"
    }
    $startup = (Get-ItemProperty -Path $startupKey -Name StreamDisplayManager -ErrorAction Stop).StreamDisplayManager
    if ($startup -notlike '*StreamDisplayManager.App.exe*') {
        throw 'Autostart registration missing or incorrect'
    }
    # This CI invocation uses /qn (UILevel=2), so the post-install launch
    # must NOT start the interactive tray process in unattended deployments.
    $running = @(Get-CimInstance Win32_Process -Filter "Name = 'StreamDisplayManager.App.exe'" -ErrorAction SilentlyContinue)
    if ($running.Count -ne 0) {
        throw "Silent MSI unexpectedly launched $($running.Count) tray process(es)"
    }
    Write-Host 'Per-user MSI install, shortcut and login startup verified.'
}
finally {
    # Always attempt cleanup, including when an assertion fails.
    if (Test-Path -LiteralPath $program) {
        Invoke-Msi '/x' $uninstallLog
    }
}

if (Test-Path -LiteralPath $program) { throw 'Uninstall retained EXE' }
if (Test-Path -LiteralPath $shortcut) { throw 'Uninstall retained Start Menu shortcut' }
$leftoverStartup = Get-ItemPropertyValue -Path $startupKey -Name StreamDisplayManager -ErrorAction SilentlyContinue
if ($null -ne $leftoverStartup) { throw 'Uninstall retained HKCU autostart' }
Write-Host 'Per-user MSI uninstall and cleanup verified.'
