#requires -PSEdition Desktop
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$assetDirectory = Join-Path $projectRoot 'src\Assets'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies @([System.Drawing.Graphics].Assembly.Location, [System.Drawing.Point].Assembly.Location) -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
public static class FloatScreenIconDrawing {
    static GraphicsPath Round(float x, float y, float w, float h, float radius) {
        var p = new GraphicsPath(); float d = radius * 2;
        p.AddArc(x, y, d, d, 180, 90); p.AddArc(x+w-d, y, d, d, 270, 90);
        p.AddArc(x+w-d, y+h-d, d, d, 0, 90); p.AddArc(x, y+h-d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }
    public static Bitmap Render(int size) {
        var image = new Bitmap(size, size);
        using (var g = Graphics.FromImage(image)) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(size / 64f, size / 64f);
            using (var path = Round(0, 0, 64, 64, 14))
            using (var fill = new SolidBrush(Color.FromArgb(31, 38, 49))) g.FillPath(fill, path);
            using (var path = Round(19, 9, 37, 30, 5))
            using (var pen = new Pen(Color.FromArgb(132, 220, 185), 2.5f)) g.DrawPath(pen, path);
            using (var pen = new Pen(Color.FromArgb(132, 220, 185), 2f)) g.DrawLine(pen, 27, 17, 47, 17);
            using (var path = Round(8, 25, 44, 29, 7))
            using (var fill = new SolidBrush(Color.FromArgb(132, 220, 185))) g.FillPath(fill, path);
            using (var pen = new Pen(Color.FromArgb(31, 38, 49), 3f)) {
                pen.StartCap = pen.EndCap = LineCap.Round;
                g.DrawLine(pen, 16, 34, 41, 34); g.DrawLine(pen, 16, 41, 35, 41);
                g.DrawLine(pen, 16, 48, 27, 48);
            }
        }
        return image;
    }
}
'@
$svg = @'
<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
  <rect width="64" height="64" rx="14" fill="#1f2631"/>
  <rect x="19" y="9" width="37" height="30" rx="5" fill="none" stroke="#84dcb9" stroke-width="2.5"/>
  <path d="M27 17h20" stroke="#84dcb9" stroke-width="2"/>
  <rect x="8" y="25" width="44" height="29" rx="7" fill="#84dcb9"/>
  <path d="M16 34h25 M16 41h19 M16 48h11" fill="none" stroke="#1f2631" stroke-width="3" stroke-linecap="round"/>
</svg>
'@
[System.IO.File]::WriteAllText((Join-Path $assetDirectory 'FloatScreen.svg'), $svg)
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
    $image = [FloatScreenIconDrawing]::Render($size)
    $stream = [System.IO.MemoryStream]::new()
    try {
        $image.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $frames.Add($stream.ToArray())
    } finally { $image.Dispose(); $stream.Dispose() }
}
$file = [System.IO.File]::Create((Join-Path $assetDirectory 'FloatScreen.ico'))
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([UInt16]1); $writer.Write([UInt16]32)
        $writer.Write([UInt32]$frames[$index].Length); $writer.Write([UInt32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
} finally { $writer.Dispose(); $file.Dispose() }
$previewDirectory = Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Path $previewDirectory -Force | Out-Null
$preview = [FloatScreenIconDrawing]::Render(256)
try { $preview.Save((Join-Path $previewDirectory 'icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
finally { $preview.Dispose() }
