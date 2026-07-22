Add-Type -AssemblyName System.Drawing

$outputDirectory = Join-Path $PSScriptRoot '..\src\Bot\Asset\branding'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$size = 512
$bitmap = New-Object System.Drawing.Bitmap($size, $size)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

function New-RoundedRectanglePath([float]$x, [float]$y, [float]$width, [float]$height, [float]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $radius * 2
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

$backgroundPath = New-RoundedRectanglePath 20 20 472 472 112
$gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Rectangle 0, 0, $size, $size),
    ([System.Drawing.Color]::FromArgb(30, 104, 225)),
    ([System.Drawing.Color]::FromArgb(111, 62, 206)),
    45)
$graphics.FillPath($gradient, $backgroundPath)

$haloBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(35, 255, 255, 255))
$graphics.FillEllipse($haloBrush, 64, 64, 384, 384)

$bubblePath = New-RoundedRectanglePath 102 132 284 210 48
$bubblePath.AddPolygon([System.Drawing.Point[]]@(
    (New-Object System.Drawing.Point 184, 326),
    (New-Object System.Drawing.Point 164, 383),
    (New-Object System.Drawing.Point 235, 342)))
$bubbleBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
$graphics.FillPath($bubbleBrush, $bubblePath)

$linePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(55, 102, 215), 18)
$linePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$linePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$graphics.DrawLine($linePen, 154, 204, 332, 204)
$graphics.DrawLine($linePen, 154, 257, 278, 257)

$sparklePen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 14)
$sparklePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$sparklePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$graphics.DrawLine($sparklePen, 385, 92, 385, 130)
$graphics.DrawLine($sparklePen, 366, 111, 404, 111)
$graphics.DrawLine($sparklePen, 92, 372, 92, 396)
$graphics.DrawLine($sparklePen, 80, 384, 104, 384)

$pngPath = Join-Path $outputDirectory 'qianNiu-bot-logo.png'
$icoPath = Join-Path $outputDirectory 'qianNiu-bot.ico'
$bitmap.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)

$imageDirectory = Join-Path $PSScriptRoot '..\src\Bot\Asset\image'
$splashPath = Join-Path $imageDirectory 'splash.gif'
$loadingPath = Join-Path $imageDirectory 'loading.gif'

$splash = New-Object System.Drawing.Bitmap(480, 360)
$splashGraphics = [System.Drawing.Graphics]::FromImage($splash)
$splashGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$splashGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$splashGradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Rectangle 0, 0, 480, 360),
    ([System.Drawing.Color]::FromArgb(244, 248, 255)),
    ([System.Drawing.Color]::FromArgb(233, 238, 255)),
    40)
$splashGraphics.FillRectangle($splashGradient, 0, 0, 480, 360)
$splashGraphics.DrawImage($bitmap, 170, 38, 140, 140)
$titleFont = New-Object System.Drawing.Font('Microsoft YaHei UI', 24, [System.Drawing.FontStyle]::Bold)
$subtitleFont = New-Object System.Drawing.Font('Microsoft YaHei UI', 11)
$titleBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(37, 56, 105))
$subtitleBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(100, 112, 145))
$format = New-Object System.Drawing.StringFormat
$format.Alignment = [System.Drawing.StringAlignment]::Center
$splashTitle = [string]::Concat([char[]]@(0x667A, 0x80FD, 0x5BA2, 0x670D, 0x52A9, 0x624B))
$splashSubtitle = [string]::Concat([char[]]@(0x6B63, 0x5728, 0x542F, 0x52A8, 0x670D, 0x52A1, 0xFF0C, 0x8BF7, 0x7A0D, 0x5019, 0x2026))
$splashGraphics.DrawString($splashTitle, $titleFont, $titleBrush, 240, 202, $format)
$splashGraphics.DrawString($splashSubtitle, $subtitleFont, $subtitleBrush, 240, 247, $format)
$dotBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(68, 97, 217))
foreach ($x in @(216, 240, 264)) { $splashGraphics.FillEllipse($dotBrush, $x, 293, 10, 10) }
$splash.Save($splashPath, [System.Drawing.Imaging.ImageFormat]::Gif)
$dotBrush.Dispose(); $format.Dispose(); $subtitleBrush.Dispose(); $titleBrush.Dispose(); $subtitleFont.Dispose(); $titleFont.Dispose()
$splashGradient.Dispose(); $splashGraphics.Dispose(); $splash.Dispose()

$loading = New-Object System.Drawing.Bitmap(118, 118)
$loadingGraphics = [System.Drawing.Graphics]::FromImage($loading)
$loadingGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$loadingGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$loadingGraphics.DrawImage($bitmap, 0, 0, 118, 118)
$loading.Save($loadingPath, [System.Drawing.Imaging.ImageFormat]::Gif)
$loadingGraphics.Dispose(); $loading.Dispose()

function Get-IcoBitmapData([System.Drawing.Bitmap]$source, [int]$iconSize) {
    $iconBitmap = New-Object System.Drawing.Bitmap($iconSize, $iconSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $iconGraphics = [System.Drawing.Graphics]::FromImage($iconBitmap)
    $iconGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $iconGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $iconGraphics.DrawImage($source, 0, 0, $iconSize, $iconSize)
    $iconGraphics.Dispose()

    $rect = New-Object System.Drawing.Rectangle(0, 0, $iconSize, $iconSize)
    $locked = $iconBitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $sourceBytes = New-Object byte[] ([Math]::Abs($locked.Stride) * $iconSize)
    [System.Runtime.InteropServices.Marshal]::Copy($locked.Scan0, $sourceBytes, 0, $sourceBytes.Length)
    $iconBitmap.UnlockBits($locked)
    $iconBitmap.Dispose()

    $pixelBytes = New-Object byte[] ($iconSize * $iconSize * 4)
    for ($row = 0; $row -lt $iconSize; $row++) {
        [Array]::Copy($sourceBytes, $row * [Math]::Abs($locked.Stride), $pixelBytes,
            ($iconSize - 1 - $row) * $iconSize * 4, $iconSize * 4)
    }
    $maskStride = [int]([Math]::Ceiling($iconSize / 32.0) * 4)
    $maskBytes = New-Object byte[] ($maskStride * $iconSize)
    $dataStream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($dataStream)
    $writer.Write([int]40)
    $writer.Write([int]$iconSize)
    $writer.Write([int]($iconSize * 2))
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([int]0)
    $writer.Write([int]$pixelBytes.Length)
    $writer.Write([int]0)
    $writer.Write([int]0)
    $writer.Write([int]0)
    $writer.Write([int]0)
    $writer.Write($pixelBytes)
    $writer.Write($maskBytes)
    $writer.Dispose()
    return ,$dataStream.ToArray()
}

$iconSizes = @(16, 24, 32, 48, 64, 128, 256)
$iconImages = @($iconSizes | ForEach-Object { Get-IcoBitmapData $bitmap $_ })
$stream = [System.IO.File]::Open($icoPath, [System.IO.FileMode]::Create)
$writer = New-Object System.IO.BinaryWriter($stream)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$iconSizes.Count)
$offset = 6 + ($iconSizes.Count * 16)
for ($index = 0; $index -lt $iconSizes.Count; $index++) {
    $entrySize = $iconSizes[$index]
    $directoryDimension = [byte]0
    if ($entrySize -ne 256) {
        $directoryDimension = [byte]$entrySize
    }
    $writer.Write($directoryDimension)
    $writer.Write($directoryDimension)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([int]$iconImages[$index].Length)
    $writer.Write([int]$offset)
    $offset += $iconImages[$index].Length
}
foreach ($iconImage in $iconImages) {
    $writer.Write($iconImage)
}
$writer.Dispose()

# Tray icons use the generated application icon.
Copy-Item -LiteralPath $icoPath -Destination (Join-Path $imageDirectory 'yellow.ico') -Force
Copy-Item -LiteralPath $icoPath -Destination (Join-Path $imageDirectory 'gray.ico') -Force
$sparklePen.Dispose()
$linePen.Dispose()
$bubbleBrush.Dispose()
$haloBrush.Dispose()
$gradient.Dispose()
$backgroundPath.Dispose()
$bubblePath.Dispose()
$graphics.Dispose()
$bitmap.Dispose()

Write-Output "Created $pngPath"
Write-Output "Created $icoPath"
