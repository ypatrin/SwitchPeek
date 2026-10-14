# Generates assets\SwitchPeek.ico (multi-size, PNG-compressed entries).
# Usage: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
param([string]$Out = (Join-Path $PSScriptRoot '..\assets\SwitchPeek.ico'))

Add-Type -AssemblyName System.Drawing
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = @()

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)

    $inset = [Math]::Max(0.5, $s * 0.05)
    $rect = New-Object System.Drawing.RectangleF $inset, $inset, ($s - 2 * $inset), ($s - 2 * $inset)
    $d = $s * 0.44
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.Left, $rect.Top, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Top, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.Left, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect,
        ([System.Drawing.Color]::FromArgb(255, 74, 120, 250)),
        ([System.Drawing.Color]::FromArgb(255, 150, 84, 240)), 45
    $g.FillPath($brush, $path)

    $text = if ($s -le 24) { 'A' } else { 'Aa' }
    $fontPx = if ($s -le 24) { $s * 0.62 } else { $s * 0.44 }
    $font = New-Object System.Drawing.Font 'Segoe UI Semibold', $fontPx, ([System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
    $textRect = New-Object System.Drawing.RectangleF 0, ($s * 0.02), $s, $s
    $g.DrawString($text, $font, [System.Drawing.Brushes]::White, $textRect, $sf)

    $g.Dispose(); $font.Dispose(); $brush.Dispose(); $path.Dispose()
    $ms = New-Object System.IO.MemoryStream
    if ($s -ge 128) {
        # Large sizes as PNG (Windows Vista+ reads them).
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        # Small sizes as classic 32-bit DIB: System.Drawing.Icon can't decode PNG entries.
        $bw = New-Object System.IO.BinaryWriter $ms
        $bw.Write([UInt32]40); $bw.Write([Int32]$s); $bw.Write([Int32]($s * 2))
        $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]0)
        $bw.Write([UInt32]0); $bw.Write([Int32]0); $bw.Write([Int32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0)
        for ($y = $s - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $s; $x++) {
                $c = $bmp.GetPixel($x, $y)
                $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
            }
        }
        $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
        $bw.Write((New-Object byte[] ($maskRow * $s)))
        $bw.Flush()
    }
    $bmp.Dispose()
    $images += , @{ Size = $s; Bytes = $ms.ToArray() }
}

$dir = Split-Path $Out
if (-not (Test-Path $dir)) { New-Item -ItemType Directory $dir | Out-Null }
$fs = [System.IO.File]::Create($Out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$img.Bytes.Length); $w.Write([UInt32]$offset)
    $offset += $img.Bytes.Length
}
foreach ($img in $images) { $w.Write($img.Bytes) }
$w.Close()
Write-Host "Wrote $Out"
