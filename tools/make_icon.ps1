# app.ico を描き直す。使い方: powershell -ExecutionPolicy Bypass -File tools\make_icon.ps1 -OutDir 出力先
# 出力先に各サイズの PNG と app_new.ico ができる。確かめてから app.ico に上書きする
param([string]$OutDir)
Add-Type -AssemblyName PresentationCore, WindowsBase
$ErrorActionPreference = 'Stop'

function C([string]$hex) { [System.Windows.Media.ColorConverter]::ConvertFromString($hex) }
function B([string]$hex) { $b = New-Object System.Windows.Media.SolidColorBrush (C $hex); $b.Freeze(); $b }
function Pt($x, $y) { New-Object System.Windows.Point $x, $y }
function Rc($x, $y, $w, $h) { New-Object System.Windows.Rect $x, $y, $w, $h }

# 256x256 の座標で描く
function Draw-Icon($dc) {
    # 背景なし。写真と虫眼鏡(x 32〜235, y 48〜239)を、256 いっぱいに広げる
    $k = 1.22
    $dc.PushTransform((New-Object System.Windows.Media.MatrixTransform $k, 0, 0, $k, (4 - 32 * $k), (11.5 - 48 * $k)))

    # 写真のフレーム(白。明るい背景でも見えるように縁取り)
    $frame = New-Object System.Windows.Media.Pen (B '#1F3C73'), 5
    $dc.DrawRoundedRectangle((B '#FFFFFF'), $frame, (Rc 34.5 50.5 183 145), 13, 13)

    # 写真の中身(空・太陽・山)
    $inner = Rc 44 60 164 126
    $dc.PushClip((New-Object System.Windows.Media.RectangleGeometry $inner, 6, 6))
    $sky = New-Object System.Windows.Media.LinearGradientBrush (C '#7EC3F2'), (C '#B9E1FA'), 90
    $dc.DrawRectangle($sky, $null, $inner)
    $dc.DrawEllipse((B '#F7C948'), $null, (Pt 188 84), 14, 14)
    $m1 = [System.Windows.Media.Geometry]::Parse('M 40,190 L 100,108 L 150,190 Z')
    $m2 = [System.Windows.Media.Geometry]::Parse('M 96,190 L 150,132 L 214,190 Z')
    $m3 = [System.Windows.Media.Geometry]::Parse('M 40,190 L 64,150 L 92,190 Z')
    $dc.DrawGeometry((B '#4FA37A'), $null, $m1)
    $dc.DrawGeometry((B '#3A8A63'), $null, $m2)
    $dc.DrawGeometry((B '#2F7553'), $null, $m3)
    $dc.Pop()

    # 虫眼鏡(右下)
    $cx = 166; $cy = 160; $r = 46
    # 持ち手(紺の縁取りのオレンジ)
    $h1 = New-Object System.Windows.Media.Pen (B '#1F3C73'), 28
    $h1.StartLineCap = 'Round'; $h1.EndLineCap = 'Round'
    $dc.DrawLine($h1, (Pt ($cx + 36) ($cy + 36)), (Pt 218 220))
    $h2 = New-Object System.Windows.Media.Pen (B '#F2A33A'), 16
    $h2.StartLineCap = 'Round'; $h2.EndLineCap = 'Round'
    $dc.DrawLine($h2, (Pt ($cx + 44) ($cy + 44)), (Pt 218 220))

    # レンズ(中は画像に隠れたプロンプト = 文章の行。2 行目はタグのチップ)
    $dc.DrawEllipse((B '#F4F8FD'), $null, (Pt $cx $cy), $r, $r)
    $line = New-Object System.Windows.Media.Pen (B '#1F3C73'), 9
    $line.StartLineCap = 'Round'; $line.EndLineCap = 'Round'
    $dc.DrawLine($line, (Pt ($cx - 26) ($cy - 18)), (Pt ($cx + 26) ($cy - 18)))
    $dc.DrawRoundedRectangle((B '#3B7BD6'), $null, (Rc ($cx - 31) ($cy - 7) 38 15), 7.5, 7.5)
    $dc.DrawLine($line, (Pt ($cx + 18) ($cy + 0.5)), (Pt ($cx + 26) ($cy + 0.5)))
    $dc.DrawLine($line, (Pt ($cx - 26) ($cy + 19)), (Pt ($cx + 10) ($cy + 19)))

    # レンズの枠
    $ring = New-Object System.Windows.Media.Pen (B '#FFFFFF'), 12
    $dc.DrawEllipse($null, $ring, (Pt $cx $cy), $r, $r)
    $ring2 = New-Object System.Windows.Media.Pen (B '#1F3C73'), 5
    $dc.DrawEllipse($null, $ring2, (Pt $cx $cy), ($r + 8.5), ($r + 8.5))
    $dc.Pop()
}

function Render([int]$size) {
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $s = $size / 256.0
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform $s, $s))
    Draw-Icon $dc
    $dc.Pop()
    $dc.Close()
    $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($dv)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = New-Object IO.MemoryStream
    $enc.Save($ms)
    , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @{}
foreach ($sz in $sizes) {
    $pngs[$sz] = Render $sz
    [IO.File]::WriteAllBytes((Join-Path $OutDir "icon_new_$sz.png"), $pngs[$sz])
}

# .ico を組み立てる(中身は PNG)
$ms = New-Object IO.MemoryStream
$bw = New-Object IO.BinaryWriter $ms
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($sz in $sizes) {
    $d = $pngs[$sz]
    $wb = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$wb); $bw.Write([byte]$wb); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$d.Length); $bw.Write([UInt32]$offset)
    $offset += $d.Length
}
foreach ($sz in $sizes) { $bw.Write($pngs[$sz]) }
$bw.Flush()
[IO.File]::WriteAllBytes((Join-Path $OutDir 'app_new.ico'), $ms.ToArray())
"ok"
