param(
    [Parameter(Mandatory = $true)] [string] $MsiPath,
    [Parameter(Mandatory = $true)] [string] $ExePath
)
$ErrorActionPreference = "Stop"
$msi = (Resolve-Path $MsiPath).Path
$exe = (Resolve-Path $ExePath).Path
$installedExe = Join-Path $env:LOCALAPPDATA 'StreamDisplayManager\StreamDisplayManager.Native.exe'
$installLog = Join-Path $env:RUNNER_TEMP 'sdm-install.log'
$uninstallLog = Join-Path $env:RUNNER_TEMP 'sdm-uninstall.log'
$first = $null
$second = $null
$installed = $false
$quote = [char]34

function Invoke-Msi([string] $Arguments, [string] $Log) {
    $quotedLog = [char]34 + $Log + [char]34
    $p = Start-Process -FilePath 'msiexec.exe' -ArgumentList "$Arguments /qn /norestart /l*v $quotedLog" -Wait -PassThru
    if ($p.ExitCode -ne 0) {
        if (Test-Path $Log) {
            Write-Host 'MSI diagnostic error context:'
            $lines = @(Get-Content $Log)
            $matches = @(for($i=0;$i -lt $lines.Count;$i++) {
                if($lines[$i] -match 'Return value 3|error 1[0-9]{3}|failed|Wix4CloseApplications|LaunchInstalledTray|CloseRunningTray|WixUnelevatedShellExec') { $i }
            })
            foreach($i in @($matches | Select-Object -Last 14)) {
                $start=[Math]::Max(0,$i-3)
                $end=[Math]::Min($lines.Count-1,$i+3)
                for($n=$start;$n -le $end;$n++) { Write-Host $lines[$n] }
                Write-Host '---'
            }
            Write-Host 'MSI diagnostic log (last 20 lines):'
            Get-Content $Log -Tail 20 | ForEach-Object { Write-Host $_ }
        }
        throw "MSI failed with exit code $($p.ExitCode)"
    }
}
try {
    # A tray started outside the destination folder must gracefully exit.
    $first = Start-Process -FilePath $exe -PassThru
    Start-Sleep -Seconds 3
    $first.Refresh()
    if ($first.HasExited) { throw 'Test tray exited before installation' }

    Invoke-Msi "/i $quote$msi$quote" $installLog
    $installed = $true
    $first.Refresh()
    if (-not $first.HasExited) { throw 'Install did not close existing tray' }
    if (-not (Test-Path $installedExe)) { throw "Installed EXE missing: $installedExe" }
    Write-Host 'Install: existing tray closed, new executable present'

    # Same close behavior should apply when removing an installed tray.
    $second = Start-Process -FilePath $installedExe -PassThru
    Start-Sleep -Seconds 3
    $second.Refresh()
    if ($second.HasExited) { throw 'Installed tray exited unexpectedly' }
    Invoke-Msi "/x $quote$msi$quote" $uninstallLog
    $installed = $false
    $second.Refresh()
    if (-not $second.HasExited) { throw 'Uninstall did not close installed tray' }
    if (Test-Path $installedExe) { throw 'Uninstall left EXE behind' }
    Write-Host 'Uninstall: tray closed, executable removed'
} finally {
    foreach($p in @($first,$second)) {
        if ($null -ne $p) {
            $p.Refresh()
            if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
        }
    }
    if ($installed) {
        $p = Start-Process -FilePath 'msiexec.exe' -ArgumentList "/x $quote$msi$quote /qn /norestart" -Wait -PassThru
        Write-Host "Cleanup uninstall exit code: $($p.ExitCode)"
    }
}
