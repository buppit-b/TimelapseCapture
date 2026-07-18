# FrameWrite icon v2: BMP entries for 16-128 (the classic format every consumer handles),
# PNG entry only for 256 (the one slot where PNG is standard). Same artwork as v1.
Add-Type -AssemblyName System.Drawing

function Draw-Tile([int]$sz) {
    $bmp = New-Object System.Drawing.Bitmap($sz, $sz, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $bg = [System.Drawing.Color]::FromArgb(255, 13, 17, 23)
    $stroke = [System.Drawing.Color]::FromArgb(255, 48, 58, 71)
    $r = [Math]::Max(2, [int]($sz * 0.18))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $w = $sz - 1
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($w - $d, 0, $d, $d, 270, 90)
    $path.AddArc($w - $d, $w - $d, $d, $d, 0, 90)
    $path.AddArc(0, $w - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $bgBrush = New-Object System.Drawing.SolidBrush($bg)
    $g.FillPath($bgBrush, $path)
    if ($sz -ge 24) {
        $pen = New-Object System.Drawing.Pen($stroke, [Math]::Max(1, $sz / 64))
        $g.DrawPath($pen, $path)
        $pen.Dispose()
    }
    if ($sz -ge 24) {
        $br = [System.Drawing.Color]::FromArgb(255, 110, 122, 138)
        $t = [Math]::Max(1.5, $sz / 16.0)
        $bpen = New-Object System.Drawing.Pen($br, $t)
        $bpen.StartCap = 'Round'; $bpen.EndCap = 'Round'
        $m = $sz * 0.20
        $len = $sz * 0.16
        $x2 = $sz - $m
        $g.DrawLine($bpen, $m, $m + $len, $m, $m); $g.DrawLine($bpen, $m, $m, $m + $len, $m)
        $g.DrawLine($bpen, $x2 - $len, $m, $x2, $m); $g.DrawLine($bpen, $x2, $m, $x2, $m + $len)
        $g.DrawLine($bpen, $m, $x2 - $len, $m, $x2); $g.DrawLine($bpen, $m, $x2, $m + $len, $x2)
        $g.DrawLine($bpen, $x2 - $len, $x2, $x2, $x2); $g.DrawLine($bpen, $x2, $x2 - $len, $x2, $x2)
        $bpen.Dispose()
    }
    $green = [System.Drawing.Color]::FromArgb(255, 63, 185, 80)
    $dotR = $sz * 0.20
    $cx = $sz / 2.0
    $dotBrush = New-Object System.Drawing.SolidBrush($green)
    $g.FillEllipse($dotBrush, [float]($cx - $dotR), [float]($cx - $dotR), [float]($dotR * 2), [float]($dotR * 2))
    if ($sz -ge 48) {
        $ring = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(140, 63, 185, 80), [Math]::Max(1.5, $sz / 40.0))
        $rr = $dotR * 1.55
        $g.DrawEllipse($ring, [float]($cx - $rr), [float]($cx - $rr), [float]($rr * 2), [float]($rr * 2))
        $ring.Dispose()
    }
    $g.Dispose(); $bgBrush.Dispose(); $path.Dispose()
    return ,$bmp
}

# Classic ICO BMP payload: BITMAPINFOHEADER (height doubled) + BGRA rows bottom-up + zero AND mask.
function To-BmpEntry([System.Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $maskStride = [int]([Math]::Ceiling($s / 32.0) * 4)
    $bw.Write([UInt32]40)                  # biSize
    $bw.Write([Int32]$s)                   # biWidth
    $bw.Write([Int32]($s * 2))             # biHeight (XOR + AND)
    $bw.Write([UInt16]1)                   # biPlanes
    $bw.Write([UInt16]32)                  # biBitCount
    $bw.Write([UInt32]0)                   # biCompression BI_RGB
    $bw.Write([UInt32]($s * $s * 4 + $maskStride * $s))
    $bw.Write([Int32]0); $bw.Write([Int32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0)
    # LockBits for speed + exact BGRA byte order
    $rect = New-Object System.Drawing.Rectangle(0, 0, $s, $s)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $rowBytes = New-Object byte[] ($s * 4)
    for ($y = $s - 1; $y -ge 0; $y--) {    # bottom-up
        [System.Runtime.InteropServices.Marshal]::Copy([IntPtr]::Add($data.Scan0, $y * $data.Stride), $rowBytes, 0, $s * 4)
        $bw.Write($rowBytes)
    }
    $bmp.UnlockBits($data)
    $mask = New-Object byte[] ($maskStride * $s)   # all zero: alpha channel rules
    $bw.Write($mask)
    $bw.Flush()
    return ,([byte[]]$ms.ToArray())
}

function To-PngEntry([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return ,([byte[]]$ms.ToArray())
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$entries = @()
foreach ($s in $sizes) {
    $bmp = Draw-Tile $s
    $payload = if ($s -ge 256) { To-PngEntry $bmp } else { To-BmpEntry $bmp }
    $entries += ,@{ Size = $s; Data = [byte[]]$payload }
    $bmp.Dispose()
}

$outDir = "C:\Users\Spike\source\TimelapseCapture\FrameWrite.Wpf\Assets"
New-Item -ItemType Directory -Force $outDir | Out-Null
$out = Join-Path $outDir "framewrite.ico"
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $s = $e.Size
    [byte[]]$data = $e.Data
    $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($e in $entries) { [byte[]]$payload = $e.Data; $bw.Write($payload) }
$bw.Close(); $fs.Close()

# Sanity: parse back at several sizes AND rasterize (this is what failed with PNG-only entries).
$preview = "C:\Users\Spike\AppData\Local\Temp\claude\C--Users-Spike-source-TimelapseCapture\57d4817a-6af4-4e02-acbf-f1b5e47e62f7\scratchpad\icon_preview.png"
$strip = New-Object System.Drawing.Bitmap(300, 152)
$g = [System.Drawing.Graphics]::FromImage($strip)
$g.Clear([System.Drawing.Color]::FromArgb(255, 90, 95, 100))
$x = 12
foreach ($s in 16, 32, 48, 128) {
    $icon = New-Object System.Drawing.Icon($out, $s, $s)
    $b = $icon.ToBitmap()
    $g.DrawImage($b, [int]$x, [int]((152 - $s) / 2), [int]$s, [int]$s)
    $x += $s + 12
    $b.Dispose(); $icon.Dispose()
}
$g.Dispose()
$strip.Save($preview, [System.Drawing.Imaging.ImageFormat]::Png)
$strip.Dispose()
Write-Output "icon: $((Get-Item $out).Length) bytes; preview OK: $preview"
