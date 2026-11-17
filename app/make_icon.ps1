# 生成应用图标：墨绿圆角方块 + 白色雷达环，多尺寸 ico
param([string]$Out)
Add-Type -AssemblyName System.Drawing
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($n in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap $n, $n
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $g.Clear([System.Drawing.Color]::Transparent)
  $r = [Math]::Max(2, [int]($n * 0.22))
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = $r * 2; $w = $n - 1
  $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($w - $d, 0, $d, $d, 270, 90)
  $path.AddArc($w - $d, $w - $d, $d, $d, 0, 90); $path.AddArc(0, $w - $d, $d, $d, 90, 90)
  $path.CloseFigure()
  $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(14, 138, 95))), $path)
  $pw = [Math]::Max(1.2, $n * 0.07)
  $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $pw
  $c = $n / 2
  foreach ($k in 0.30, 0.17) { $rr = $n * $k; $g.DrawEllipse($pen, [single]($c - $rr), [single]($c - $rr), [single]($rr * 2), [single]($rr * 2)) }
  $dot = $n * 0.07
  $g.FillEllipse([System.Drawing.Brushes]::White, [single]($c - $dot), [single]($c - $dot), [single]($dot * 2), [single]($dot * 2))
  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  $pngs += , $ms.ToArray()
}
$fs = [System.IO.File]::Create($Out); $bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$off = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
  $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$off)
  $off += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
