param(
    [string]$SourcePng = (Join-Path $PSScriptRoot '..\Resources\TaskFolderIcon.png'),
    [string]$OutputIco = (Join-Path $PSScriptRoot '..\Resources\TaskFolder.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$sourcePath = (Resolve-Path -LiteralPath $SourcePng).Path
$outputPath = [IO.Path]::GetFullPath($OutputIco)
$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
$frames = [Collections.Generic.List[byte[]]]::new()
$source = [Drawing.Bitmap]::FromFile($sourcePath)
try {
    if ($source.Width -ne $source.Height) { throw 'The application icon source must be square.' }
    foreach ($size in $sizes) {
        $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $attributes = [Drawing.Imaging.ImageAttributes]::new()
        $stream = [IO.MemoryStream]::new()
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $attributes.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)
            $target = [Drawing.Rectangle]::new(0, 0, $size, $size)
            $graphics.DrawImage($source, $target, 0, 0, $source.Width, $source.Height,
                [Drawing.GraphicsUnit]::Pixel, $attributes)
            if ($size -eq 256) {
                $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            }
            else {
                # Standard 32-bit DIB + AND mask for native Shell/installer extractors.
                $frameWriter = [IO.BinaryWriter]::new($stream, [Text.Encoding]::UTF8, $true)
                try {
                    $frameWriter.Write([uint32]40)
                    $frameWriter.Write([int32]$size)
                    $frameWriter.Write([int32]($size * 2))
                    $frameWriter.Write([uint16]1)
                    $frameWriter.Write([uint16]32)
                    $frameWriter.Write([uint32]0)
                    $frameWriter.Write([uint32]($size * $size * 4))
                    1..4 | ForEach-Object { $frameWriter.Write([uint32]0) }
                    $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
                    $mask = [byte[]]::new($maskStride * $size)
                    for ($y = $size - 1; $y -ge 0; $y--) {
                        for ($x = 0; $x -lt $size; $x++) {
                            $color = $bitmap.GetPixel($x, $y)
                            $frameWriter.Write($color.B)
                            $frameWriter.Write($color.G)
                            $frameWriter.Write($color.R)
                            $frameWriter.Write($color.A)
                            if ($color.A -eq 0) {
                                $offsetInMask = ($size - 1 - $y) * $maskStride + [int][Math]::Floor($x / 8.0)
                                $mask[$offsetInMask] = $mask[$offsetInMask] -bor (0x80 -shr ($x % 8))
                            }
                        }
                    }
                    $frameWriter.Write($mask)
                }
                finally { $frameWriter.Dispose() }
            }
            $frames.Add($stream.ToArray())
        }
        finally {
            $stream.Dispose()
            $attributes.Dispose()
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }
}
finally { $source.Dispose() }

# Small frames use alpha-preserving DIBs; only the 256 px frame uses PNG compression.
# This is format conversion of the approved artwork, with no image-generation step.
$output = [IO.File]::Create($outputPath)
$writer = [IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    [uint32]$offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        [byte]$dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write($dimension)
        $writer.Write($dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length)
        $writer.Write($offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
}
finally { $writer.Dispose() }
Write-Output "Created $outputPath ($($sizes -join ', ') px) from $sourcePath"
