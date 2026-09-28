$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path $PSScriptRoot '../SCNexus/Assets'
New-Item -ItemType Directory -Force $assetDirectory | Out-Null
$svg = @'
<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <rect x="8" y="8" width="240" height="240" rx="54" fill="#101e2b"/>
  <rect x="9" y="9" width="238" height="238" rx="53" fill="none" stroke="#2b4657" stroke-width="2"/>
  <path d="M65 183V76L179 183V77" fill="none" stroke="#78dbcb" stroke-width="20" stroke-linejoin="round" stroke-linecap="round"/>
  <circle cx="65" cy="183" r="14" fill="#effbf9"/>
  <circle cx="65" cy="76" r="14" fill="#effbf9"/>
  <circle cx="179" cy="183" r="14" fill="#effbf9"/>
  <path d="M179 43L207 93L179 82L151 93Z" fill="#78dbcb"/>
</svg>
'@
[IO.File]::WriteAllText((Join-Path $assetDirectory 'sc-nexus.svg'), $svg)
$master = [Drawing.Bitmap]::new(1024,1024)
$g = [Drawing.Graphics]::FromImage($master)
$g.SmoothingMode = 'AntiAlias'
$g.ScaleTransform(4,4)
$path = [Drawing.Drawing2D.GraphicsPath]::new()
foreach ($arc in @(@(8,8,108,108,180),@(140,8,108,108,270),@(140,140,108,108,0),@(8,140,108,108,90))) {
  $path.AddArc($arc[0],$arc[1],$arc[2],$arc[3],$arc[4],90)
}
$path.CloseFigure()
$background = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#101e2b'))
$mint = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#78dbcb'))
$white = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#effbf9'))
$border = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#2b4657'),2)
$g.FillPath($background,$path); $g.DrawPath($border,$path)
$pen = [Drawing.Pen]::new($mint,20)
$pen.LineJoin = 'Round'; $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
$g.DrawLines($pen,[Drawing.PointF[]]@([Drawing.PointF]::new(65,183),[Drawing.PointF]::new(65,76),[Drawing.PointF]::new(179,183),[Drawing.PointF]::new(179,77)))
foreach($point in @(@(65,183),@(65,76),@(179,183))) { $g.FillEllipse($white,($point[0]-14),($point[1]-14),28,28) }
$g.FillPolygon($mint,[Drawing.PointF[]]@([Drawing.PointF]::new(179,43),[Drawing.PointF]::new(207,93),[Drawing.PointF]::new(179,82),[Drawing.PointF]::new(151,93)))
$g.Dispose()
$sizes = @(16,24,32,48,64,128,256)
$images = [Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
  $bitmap = [Drawing.Bitmap]::new($size,$size)
  $canvas = [Drawing.Graphics]::FromImage($bitmap)
  $canvas.InterpolationMode = 'HighQualityBicubic'; $canvas.PixelOffsetMode = 'HighQuality'
  $canvas.DrawImage($master,0,0,$size,$size)
  $stream = [IO.MemoryStream]::new()
  $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
  $images.Add($stream.ToArray())
  if($size -eq 256) { $bitmap.Save((Join-Path $assetDirectory 'sc-nexus.png'),[Drawing.Imaging.ImageFormat]::Png) }
  $stream.Dispose(); $canvas.Dispose(); $bitmap.Dispose()
}
$file = [IO.File]::Create((Join-Path $assetDirectory 'sc-nexus.ico'))
$writer = [IO.BinaryWriter]::new($file)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for($i=0;$i -lt $sizes.Count;$i++) {
  $dimension = if($sizes[$i] -eq 256){0}else{$sizes[$i]}
  $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
  $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
  $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
  $offset += $images[$i].Length
}
foreach($bytes in $images) { $writer.Write($bytes) }
$writer.Dispose(); $master.Dispose(); $path.Dispose(); $background.Dispose(); $mint.Dispose(); $white.Dispose(); $border.Dispose(); $pen.Dispose()
