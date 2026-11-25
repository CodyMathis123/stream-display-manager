param([Parameter(Mandatory=$true)][string]$IconPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$bytes = [System.IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $IconPath).Path)
if ([BitConverter]::ToUInt16($bytes, 2) -ne 1) { throw 'Not a Windows ICO' }
$count = [BitConverter]::ToUInt16($bytes, 4)
if ($count -ne 6) { throw "Expected six ICO frames; got $count" }
$expected = @(16,24,32,48,64,256)
for ($i=0; $i -lt $count; $i++) {
    $size = [int]$bytes[6 + 16*$i]
    if ($size -eq 0) { $size=256 }
    if ($size -ne $expected[$i]) { throw "Unexpected frame size: $size" }
}
$length=[BitConverter]::ToUInt32($bytes,14)
$offset=[BitConverter]::ToUInt32($bytes,18)
$stream=[System.IO.MemoryStream]::new($bytes,[int]$offset,[int]$length,$false)
try {
    $image=[System.Drawing.Bitmap]::new($stream)
    try {
        if ($image.Width -ne 16 -or $image.Height -ne 16) {
            throw 'First frame must be 16x16'
        }
        $cyan=0
        $white=0
        $navy=0
        for ($y=0; $y -lt 16; $y++) {
            for ($x=0; $x -lt 16; $x++) {
                $p=$image.GetPixel($x,$y)
                if ($p.A -lt 128) { continue }
                if ($p.G -gt 150 -and $p.B -gt 130 -and $p.R -lt 160) { $cyan++ }
                elseif ($p.R -gt 210 -and $p.G -gt 210 -and $p.B -gt 210) { $white++ }
                elseif ($p.R -lt 65 -and $p.G -lt 95 -and $p.B -lt 115) { $navy++ }
            }
        }
        if ($cyan -lt 10 -or $white -lt 3 -or $navy -lt 30) {
            throw "16px glyph lacks contrast: cyan=$cyan white=$white navy=$navy"
        }
        Write-Host "16px icon contrast verified: cyan=$cyan white=$white navy=$navy."
    }
    finally { $image.Dispose() }
}
finally { $stream.Dispose() }
