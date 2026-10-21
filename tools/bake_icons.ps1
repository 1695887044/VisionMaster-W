# bake_icons.ps1 -- bake the app / help-dialog window icons (Alt-Tab + taskbar).
# Pure ASCII on purpose (AGENTS.md R1). Run with:
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File tools\bake_icons.ps1
#
# Why a baker at all: the repo had NO icon asset anywhere (no .ico, no ApplicationIcon),
# so every window -- main shell and every dialog -- showed the same default Windows icon.
# Icons are authored as VECTOR here and rendered straight to each target size, so the
# 16px entry is drawn at 16px (not a shrunk 256px, which is what makes taskbar icons muddy).

param(
    [string]$IconDir = 'VisionMaster\Resources\Icons',
    [string]$PreviewDir = '_icon_preview'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName WindowsBase

# 24x24 design units; every size is rendered by scaling this canvas.
$Unit = 24.0
$Sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

function New-RoundedRectGeometry([double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
    $g = New-Object System.Windows.Media.RectangleGeometry
    $g.Rect = New-Object System.Windows.Rect($x, $y, $w, $h)
    $g.RadiusX = $r
    $g.RadiusY = $r
    return $g
}

# Help tile background: sky-blue family, same accent the dialogs use (#0EA5E9).
function New-TileBackground {
    $grad = New-Object System.Windows.Media.LinearGradientBrush
    $grad.StartPoint = New-Object System.Windows.Point(0, 0)
    $grad.EndPoint = New-Object System.Windows.Point(0, 1)
    $g1 = New-Object System.Windows.Media.GradientStop
    $g1.Color = [System.Windows.Media.Color]::FromRgb(0x38, 0xBD, 0xF8)
    $g1.Offset = 0.0
    $g2 = New-Object System.Windows.Media.GradientStop
    $g2.Color = [System.Windows.Media.Color]::FromRgb(0x02, 0x84, 0xC7)
    $g2.Offset = 1.0
    [void]$grad.GradientStops.Add($g1)
    [void]$grad.GradientStops.Add($g2)
    $d = New-Object System.Windows.Media.GeometryDrawing
    $d.Brush = $grad
    $d.Geometry = New-RoundedRectGeometry 0 0 $Unit $Unit 5.4
    return $d
}

# App tile: deep teal -> ink (not the dialog sky blue) so the cyan focal dot reads as the accent.
function New-AppTileBackground {
    $grad = New-Object System.Windows.Media.LinearGradientBrush
    $grad.StartPoint = New-Object System.Windows.Point(0.1, 0)
    $grad.EndPoint = New-Object System.Windows.Point(0.9, 1)
    $stops = @(
        @(0.0, 0x0C, 0x73, 0x96),
        @(0.55, 0x08, 0x53, 0x6F),
        @(1.0, 0x05, 0x22, 0x34)
    )
    foreach ($s in $stops) {
        $g = New-Object System.Windows.Media.GradientStop
        $g.Color = [System.Windows.Media.Color]::FromRgb($s[1], $s[2], $s[3])
        $g.Offset = $s[0]
        [void]$grad.GradientStops.Add($g)
    }
    $d = New-Object System.Windows.Media.GeometryDrawing
    $d.Brush = $grad
    $d.Geometry = New-RoundedRectGeometry 0 0 $Unit $Unit 5.4
    return $d
}

# App mark: four open corner brackets + a cyan focal dot (a vision registration mark).
# Corner arcs are quarter circles (r=2.6), so the gaps survive the 16px raster.
function New-AppDrawing {
    $group = New-Object System.Windows.Media.DrawingGroup
    [void]$group.Children.Add((New-AppTileBackground))

    $glow = New-Object System.Windows.Media.RadialGradientBrush
    $glow.Center = New-Object System.Windows.Point(0.5, 0.5)
    $glow.GradientOrigin = New-Object System.Windows.Point(0.5, 0.5)
    $glow.RadiusX = 0.5
    $glow.RadiusY = 0.5
    $glowStops = @(
        @(0.0, 128),
        @(0.6, 33),
        @(1.0, 0)
    )
    foreach ($s in $glowStops) {
        $g = New-Object System.Windows.Media.GradientStop
        $g.Color = [System.Windows.Media.Color]::FromArgb($s[1], 0x38, 0xBD, 0xF8)
        $g.Offset = $s[0]
        [void]$glow.GradientStops.Add($g)
    }
    $glowGeo = New-Object System.Windows.Media.EllipseGeometry
    $glowGeo.Center = New-Object System.Windows.Point(12, 12)
    $glowGeo.RadiusX = 7.8
    $glowGeo.RadiusY = 7.8
    $glowDrawing = New-Object System.Windows.Media.GeometryDrawing
    $glowDrawing.Brush = $glow
    $glowDrawing.Geometry = $glowGeo
    [void]$group.Children.Add($glowDrawing)

    $inkBrush = New-Object System.Windows.Media.SolidColorBrush
    $inkBrush.Color = [System.Windows.Media.Color]::FromRgb(0xF8, 0xFA, 0xFC)
    $pen = New-Object System.Windows.Media.Pen
    $pen.Brush = $inkBrush
    $pen.Thickness = 2.3
    $pen.StartLineCap = [System.Windows.Media.PenLineCap]::Round
    $pen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
    $pen.LineJoin = [System.Windows.Media.PenLineJoin]::Round

    $path = New-Object System.Windows.Media.PathGeometry
    $corners = @(
        @(4.5, 9.1, 4.5, 7.1, 7.1, 4.5, 9.1, 4.5),
        @(14.9, 4.5, 16.9, 4.5, 19.5, 7.1, 19.5, 9.1),
        @(19.5, 14.9, 19.5, 16.9, 16.9, 19.5, 14.9, 19.5),
        @(9.1, 19.5, 7.1, 19.5, 4.5, 16.9, 4.5, 14.9)
    )
    foreach ($c in $corners) {
        $fig = New-Object System.Windows.Media.PathFigure
        $fig.StartPoint = New-Object System.Windows.Point($c[0], $c[1])
        $fig.IsFilled = $false
        $fig.IsClosed = $false

        $l1 = New-Object System.Windows.Media.LineSegment
        $l1.Point = New-Object System.Windows.Point($c[2], $c[3])
        $l1.IsStroked = $true
        [void]$fig.Segments.Add($l1)

        $arc = New-Object System.Windows.Media.ArcSegment
        $arc.Point = New-Object System.Windows.Point($c[4], $c[5])
        $arc.Size = New-Object System.Windows.Size(2.6, 2.6)
        $arc.RotationAngle = 0
        $arc.IsLargeArc = $false
        $arc.SweepDirection = [System.Windows.Media.SweepDirection]::Clockwise
        $arc.IsStroked = $true
        [void]$fig.Segments.Add($arc)

        $l2 = New-Object System.Windows.Media.LineSegment
        $l2.Point = New-Object System.Windows.Point($c[6], $c[7])
        $l2.IsStroked = $true
        [void]$fig.Segments.Add($l2)

        [void]$path.Figures.Add($fig)
    }

    $frame = New-Object System.Windows.Media.GeometryDrawing
    $frame.Pen = $pen
    $frame.Geometry = $path
    [void]$group.Children.Add($frame)

    $dotBrush = New-Object System.Windows.Media.SolidColorBrush
    $dotBrush.Color = [System.Windows.Media.Color]::FromRgb(0x38, 0xBD, 0xF8)
    $dot = New-Object System.Windows.Media.GeometryDrawing
    $dot.Brush = $dotBrush
    $dotGeo = New-Object System.Windows.Media.EllipseGeometry
    $dotGeo.Center = New-Object System.Windows.Point(12, 12)
    $dotGeo.RadiusX = 2.9
    $dotGeo.RadiusY = 2.9
    $dot.Geometry = $dotGeo
    [void]$group.Children.Add($dot)

    $coreBrush = New-Object System.Windows.Media.SolidColorBrush
    $coreBrush.Color = [System.Windows.Media.Color]::FromRgb(0xE6, 0xFA, 0xFF)
    $core = New-Object System.Windows.Media.GeometryDrawing
    $core.Brush = $coreBrush
    $coreGeo = New-Object System.Windows.Media.EllipseGeometry
    $coreGeo.Center = New-Object System.Windows.Point(12, 12)
    $coreGeo.RadiusX = 1.15
    $coreGeo.RadiusY = 1.15
    $core.Geometry = $coreGeo
    [void]$group.Children.Add($core)

    return $group
}

# Help mark: the SAME book glyph the dialog header badge draws (Font Awesome 6 Pro Solid
# U+F02D, see UI\Controls\Asserts\FontFamilys), so the taskbar tile and the in-window
# badge read as one thing. GlyphRunDrawing keeps it vector -> no TextBlock/Visual tree.
function New-HelpDrawing {
    $fontPath = Join-Path $PSScriptRoot '..\UI\Controls\Asserts\FontFamilys\Font Awesome 6 Pro-Solid-900.otf'
    if (-not (Test-Path $fontPath)) { throw "icon font not found: $fontPath" }
    $uri = New-Object System.Uri((Resolve-Path $fontPath).Path)
    # A bare .otf uri is NOT a FontFamily key -- WPF wants "<file uri>#<family name>".
    # AbsoluteUri already percent-encodes the '#' in this repo's root path (D:\C#).
    $familyKey = $uri.AbsoluteUri + '#Font Awesome 6 Pro Solid'
    $family = New-Object System.Windows.Media.FontFamily($familyKey)
    $tf = New-Object System.Windows.Media.Typeface($family,
        [System.Windows.FontStyles]::Normal, [System.Windows.FontWeights]::Normal, [System.Windows.FontStretches]::Normal)

    $text = [string][char]0xF02D
    $em = 14.0
    $ft = New-Object System.Windows.Media.FormattedText($text,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Windows.FlowDirection]::LeftToRight, $tf, $em,
        [System.Windows.Media.Brushes]::White, 1.0)

    # Center the INK box (not the layout box) on the tile: rebuild the geometry at the
    # offset that puts its bounds center on (12,12).
    $probe = $ft.BuildGeometry([System.Windows.Point]::new(0, 0)).Bounds
    $ox = $Unit / 2 - ($probe.X + $probe.Width / 2)
    $oy = $Unit / 2 - ($probe.Y + $probe.Height / 2)
    $geo = $ft.BuildGeometry([System.Windows.Point]::new($ox, $oy))

    $glyph = New-Object System.Windows.Media.GeometryDrawing
    $glyph.Brush = [System.Windows.Media.Brushes]::White
    $glyph.Geometry = $geo

    $group = New-Object System.Windows.Media.DrawingGroup
    [void]$group.Children.Add((New-TileBackground))
    [void]$group.Children.Add($glyph)
    return $group
}

function Render-Png([System.Windows.Media.Drawing]$drawing, [int]$px) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    # Grayscale AA: ClearType on a transparent canvas would fringe the icon edges.
    [System.Windows.Media.TextOptions]::SetTextRenderingMode($visual, [System.Windows.Media.TextRenderingMode]::Grayscale)
    [System.Windows.Media.TextOptions]::SetTextFormattingMode($visual, [System.Windows.Media.TextFormattingMode]::Ideal)
    $dc = $visual.RenderOpen()
    [void]$dc.PushTransform((New-Object System.Windows.Media.ScaleTransform(($px / $Unit), ($px / $Unit))))
    $dc.DrawDrawing($drawing)
    $dc.Close()
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($px, $px, 96, 96,
        [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($visual)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    [void]$enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    return , $ms.ToArray()
}

# ICO container with PNG-compressed entries (Vista+ shell). 0 in the width byte means 256.
function Write-Ico([string]$path, [byte[][]]$pngs, [int[]]$px) {
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([uint16]0)          # reserved
    $bw.Write([uint16]1)          # type: icon
    $bw.Write([uint16]$pngs.Count)
    $offset = 6 + (16 * $pngs.Count)
    for ($i = 0; $i -lt $pngs.Count; $i++) {
        $w = $px[$i]; $h = $px[$i]
        if ($w -ge 256) { $w = 0 }
        if ($h -ge 256) { $h = 0 }
        $bw.Write([byte]$w)
        $bw.Write([byte]$h)
        $bw.Write([byte]0)        # palette size
        $bw.Write([byte]0)        # reserved
        $bw.Write([uint16]1)      # planes
        $bw.Write([uint16]32)     # bit depth
        $bw.Write([uint32]$pngs[$i].Length)
        $bw.Write([uint32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $bw.Write($p) }
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($path, $ms.ToArray())
    $bw.Close()
}

function Bake([string]$name, [System.Windows.Media.Drawing]$drawing) {
    $pngs = @()
    $px = @()
    foreach ($s in $Sizes) {
        $bytes = Render-Png $drawing $s
        [System.IO.File]::WriteAllBytes((Join-Path $PreviewDir ("{0}-{1}.png" -f $name, $s)), $bytes)
        $pngs += , $bytes
        $px += $s
    }
    $ico = Join-Path $IconDir ($name + '.ico')
    Write-Ico $ico $pngs $px
    $info = Get-Item $ico
    Write-Output ("{0}: {1} entries, {2} bytes -> {3}" -f $name, $pngs.Count, $info.Length, $ico)
}

New-Item -ItemType Directory -Force -Path $IconDir | Out-Null
New-Item -ItemType Directory -Force -Path $PreviewDir | Out-Null

Bake 'visionmaster' (New-AppDrawing)
Bake 'help' (New-HelpDrawing)
Write-Output 'DONE'
