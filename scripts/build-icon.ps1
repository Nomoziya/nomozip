param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../assets'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$source = Join-Path $PSScriptRoot '../assets/nomozip-mark.xaml'
$drawing = [System.Windows.Markup.XamlReader]::Parse([IO.File]::ReadAllText($source))
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$frames = @()
foreach ($size in @(16, 20, 24, 32, 40, 48, 64, 128, 256)) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    $context.DrawImage($drawing, [System.Windows.Rect]::new(0, 0, $size, $size))
    $context.Close()
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    $encoder.Save($stream)
    $frames += ,@{ Size = $size; Bytes = $stream.ToArray() }
    $stream.Dispose()
    if ($size -eq 256) { [IO.File]::WriteAllBytes((Join-Path $OutputDirectory 'nomozip-256.png'), $frames[-1].Bytes) }
}
$stream = [IO.MemoryStream]::new()
$writer = [IO.BinaryWriter]::new($stream)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
    $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
    $offset += $frame.Bytes.Length
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
$writer.Flush()
[IO.File]::WriteAllBytes((Join-Path $OutputDirectory 'nomozip.ico'), $stream.ToArray())
$writer.Dispose(); $stream.Dispose()
Write-Output 'Generated nine transparent icon sizes from the WPF vector master.'
