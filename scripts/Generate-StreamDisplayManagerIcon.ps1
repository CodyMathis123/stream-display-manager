# Generate the Win32/MSI Stream Display Manager application icon using the same design
# as a single bold monitor with a play triangle, legible at 16 pixels.
# Creates a standard multi-resolution PNG-compressed ICO (16/24/32/48/64/256).
param([Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-IconPng([int]$Size) {
    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $scale = [single]($Size / 64.0)
            $graphics.ScaleTransform($scale, $scale)

            $background = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(14, 28, 49))
            $white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
            $cyan = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(37, 226, 222), [single]6.0)
            try {
                $cyan.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
                $cyan.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
                $cyan.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

                # A large, bold silhouette stands out on light and dark taskbars.
                $graphics.FillEllipse($background, [System.Drawing.RectangleF]::new(1.5, 1.5, 61, 61))
                $graphics.DrawRectangle($cyan, [single]12, [single]18, [single]40, [single]29)

                # The triangle fills several real pixels, even at 16x16.
                [System.Drawing.PointF[]]$triangle = @(
                    [System.Drawing.PointF]::new(27, 25),
                    [System.Drawing.PointF]::new(27, 40),
                    [System.Drawing.PointF]::new(43, 32.5)
                )
                $graphics.FillPolygon($white, $triangle)

                # Avoid one-pixel details in the 16px and 24px tray frames.
                if ($Size -ge 32) {
                    $graphics.DrawLine($cyan, [single]32, [single]49, [single]32, [single]54)
                    $graphics.DrawLine($cyan, [single]25, [single]54, [single]39, [single]54)
                }
            }
            finally { $background.Dispose(); $white.Dispose(); $cyan.Dispose() }
        }
        finally { $graphics.Dispose() }
        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            return ,$stream.ToArray()
        }
        finally { $stream.Dispose() }
    }
    finally { $bitmap.Dispose() }
}

$sizes = @(16,24,32,48,64,256)
$frames = foreach ($size in $sizes) {
    [pscustomobject]@{ Size = $size; Image = [byte[]](New-IconPng $size) }
}
$parent = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $parent)) {
    [void](New-Item -ItemType Directory -Force -Path $parent)
}
$file = [System.IO.File]::Create($OutputPath)
try {
    $writer = [System.IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0)  # reserved
        $writer.Write([uint16]1)  # ICO
        $writer.Write([uint16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)      # palette
            $writer.Write([byte]0)      # reserved
            $writer.Write([uint16]1)    # planes
            $writer.Write([uint16]32)   # bit depth
            $writer.Write([uint32]$frame.Image.Length)
            $writer.Write([uint32]$offset)
            $offset += $frame.Image.Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Image) }
    }
    finally { $writer.Dispose() }
}
finally { $file.Dispose() }
Write-Host "Generated Stream Display Manager icon: $OutputPath ($((Get-Item $OutputPath).Length) bytes)"
