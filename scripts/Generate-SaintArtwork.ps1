# SPDX-License-Identifier: MIT
# Generates symbolic saint-calendar artwork from stable event keys. Run on Windows.
param(
    [string]$DataPath = (Join-Path $PSScriptRoot '../WorkTrail.Core/Data/celestial-calendar.json'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../WorkTrail/Assets/Celestial/Artwork')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function C([int]$a, [int]$r, [int]$g, [int]$b) {
    [System.Drawing.Color]::FromArgb($a, $r, $g, $b)
}

function Fill-Ellipse($g, $color, [single]$x, [single]$y, [single]$w, [single]$h) {
    $brush = [System.Drawing.SolidBrush]::new($color)
    try { $g.FillEllipse($brush, $x, $y, $w, $h) } finally { $brush.Dispose() }
}

function Fill-Polygon($g, $color, [System.Drawing.PointF[]]$points) {
    $brush = [System.Drawing.SolidBrush]::new($color)
    try { $g.FillPolygon($brush, $points) } finally { $brush.Dispose() }
}

function Fill-GradientEllipse($g, $centerColor, $edgeColor, [single]$x, [single]$y, [single]$w, [single]$h) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddEllipse($x, $y, $w, $h)
    $brush = [System.Drawing.Drawing2D.PathGradientBrush]::new($path)
    try {
        $brush.CenterColor = $centerColor
        $brush.SurroundColors = [System.Drawing.Color[]]@($edgeColor)
        $g.FillPath($brush, $path)
    } finally { $brush.Dispose(); $path.Dispose() }
}

function Draw-Line($g, $color, [single]$width, [single]$x1, [single]$y1, [single]$x2, [single]$y2) {
    $pen = [System.Drawing.Pen]::new($color, $width)
    try { $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $g.DrawLine($pen, $x1, $y1, $x2, $y2) } finally { $pen.Dispose() }
}

function Draw-Ellipse($g, $color, [single]$width, [single]$x, [single]$y, [single]$w, [single]$h) {
    $pen = [System.Drawing.Pen]::new($color, $width)
    try { $g.DrawEllipse($pen, $x, $y, $w, $h) } finally { $pen.Dispose() }
}

function Draw-String($g, [string]$value, [string]$fontName, [single]$size, $color, [single]$x, [single]$y, [single]$w, [single]$h) {
    $font = [System.Drawing.Font]::new($fontName, $size, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $brush = [System.Drawing.SolidBrush]::new($color)
    $format = [System.Drawing.StringFormat]::new()
    try {
        $format.Alignment = 'Center'
        $format.LineAlignment = 'Center'
        $g.DrawString($value, $font, $brush, [System.Drawing.RectangleF]::new($x, $y, $w, $h), $format)
    } finally { $format.Dispose(); $brush.Dispose(); $font.Dispose() }
}

function Get-Motif([string]$key, [string]$name) {
    $both = "$key $name"
    if ($both -match 'Mary|Maria|Marian|OurLady|Assumption|Visitation|Immaculate|Guadalupe|Loreto|Rosary|Carmel') { return 'rose' }
    if ($both -match 'Peter|Petr') { return 'keys' }
    if ($both -match 'Paul|Paul') { return 'sword' }
    if ($both -match 'Francis|Frances|Francisc') { return 'bird' }
    if ($both -match 'StAgnes|\bAgnes\b|\bAgnetis\b') { return 'lamb' }
    if ($both -match 'Cecilia|Caecilia') { return 'harp' }
    if ($both -match 'Patrick|Patric') { return 'trefoil' }
    if ($both -match 'Lucy|Lucia') { return 'lamp' }
    if ($both -match 'Joseph|Ioseph') { return 'lily' }
    if ($both -match 'Nicholas|Nicol') { return 'gift' }
    if ($both -match 'George|Georg') { return 'spear' }
    if ($both -match 'Angels|Archangel|Angel') { return 'wing' }
    if ($both -match 'Evangelist|Evangelista|Doctor|doctor|doctorum|doctora') { return 'book' }
    if ($both -match 'martyr|Martyr|martyrum') { return 'palm' }
    if ($both -match 'episcop|bishop|Pope|papa|pontific') { return 'staff' }
    if ($both -match 'virgin|virgini|Virgo') { return 'lily' }
    if ($both -match 'apostol|Apostol|Ap$') { return 'scroll' }
    if ($both -match 'abbot|abbas|monach|religios') { return 'cross' }
    if ($both -match 'presbyter|priest|sacerdot') { return 'chalice' }
    # Neutral decorative emblems add variety where the source does not identify
    # an established attribute. Their choice does not assert saint iconography.
    $neutral = @('star','book','rose','lily','lamp','bird','scroll','cross','trefoil','chalice')
    $bytes = [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($key))
    return $neutral[$bytes[6] % $neutral.Count]
}

function Draw-Motif($g, [string]$motif, [single]$cx, [single]$cy, [single]$scale, $gold, $ivory) {
    # These are symbolic decorative marks, not purported historical portraits or relics.
    switch ($motif) {
        'book' {
            Fill-Polygon $g (C 230 28 48 76) @([System.Drawing.PointF]::new($cx-46*$scale,$cy-26*$scale),[System.Drawing.PointF]::new($cx,$cy-18*$scale),[System.Drawing.PointF]::new($cx,$cy+32*$scale),[System.Drawing.PointF]::new($cx-46*$scale,$cy+22*$scale))
            Fill-Polygon $g (C 230 38 57 88) @([System.Drawing.PointF]::new($cx,$cy-18*$scale),[System.Drawing.PointF]::new($cx+46*$scale,$cy-26*$scale),[System.Drawing.PointF]::new($cx+46*$scale,$cy+22*$scale),[System.Drawing.PointF]::new($cx,$cy+32*$scale))
            Draw-Line $g $gold (3*$scale) $cx ($cy-18*$scale) $cx ($cy+32*$scale)
            Draw-Line $g $ivory (2*$scale) ($cx-38*$scale) ($cy-12*$scale) ($cx-8*$scale) ($cy-7*$scale)
            Draw-Line $g $ivory (2*$scale) ($cx+9*$scale) ($cy-7*$scale) ($cx+38*$scale) ($cy-12*$scale)
        }
        'keys' {
            Draw-Ellipse $g $gold (7*$scale) ($cx-37*$scale) ($cy-46*$scale) (27*$scale) (27*$scale)
            Draw-Ellipse $g $gold (7*$scale) ($cx+10*$scale) ($cy-46*$scale) (27*$scale) (27*$scale)
            Draw-Line $g $gold (7*$scale) ($cx-23*$scale) ($cy-19*$scale) ($cx+19*$scale) ($cy+37*$scale)
            Draw-Line $g $gold (7*$scale) ($cx+23*$scale) ($cy-19*$scale) ($cx-19*$scale) ($cy+37*$scale)
            Draw-Line $g $ivory (6*$scale) ($cx+13*$scale) ($cy+27*$scale) ($cx+27*$scale) ($cy+18*$scale)
            Draw-Line $g $ivory (6*$scale) ($cx-13*$scale) ($cy+27*$scale) ($cx-27*$scale) ($cy+18*$scale)
        }
        'sword' {
            Fill-Polygon $g $ivory @([System.Drawing.PointF]::new($cx,$cy-54*$scale),[System.Drawing.PointF]::new($cx+8*$scale,$cy+24*$scale),[System.Drawing.PointF]::new($cx,$cy+36*$scale),[System.Drawing.PointF]::new($cx-8*$scale,$cy+24*$scale))
            Draw-Line $g $gold (8*$scale) ($cx-24*$scale) ($cy+24*$scale) ($cx+24*$scale) ($cy+24*$scale)
            Draw-Line $g $gold (8*$scale) $cx ($cy+30*$scale) $cx ($cy+50*$scale)
        }
        'palm' {
            Draw-Line $g $gold (5*$scale) ($cx-12*$scale) ($cy+45*$scale) ($cx+17*$scale) ($cy-43*$scale)
            foreach ($i in 0..5) {
                $y = $cy-35*$scale+$i*14*$scale
                $x = $cx+14*$scale-$i*4*$scale
                Draw-Line $g $ivory (4*$scale) $x $y ($x-29*$scale) ($y-13*$scale)
                Draw-Line $g $ivory (4*$scale) $x $y ($x+24*$scale) ($y-9*$scale)
            }
        }
        'lily' {
            Draw-Line $g $gold (5*$scale) $cx ($cy+43*$scale) $cx ($cy-20*$scale)
            Fill-Ellipse $g $ivory ($cx-12*$scale) ($cy-43*$scale) (24*$scale) (32*$scale)
            Fill-Ellipse $g $ivory ($cx-36*$scale) ($cy-24*$scale) (24*$scale) (30*$scale)
            Fill-Ellipse $g $ivory ($cx+12*$scale) ($cy-24*$scale) (24*$scale) (30*$scale)
            Draw-Line $g $gold (3*$scale) $cx ($cy-29*$scale) $cx ($cy-52*$scale)
        }
        'rose' {
            foreach ($i in 0..4) {
                $a=2*[math]::PI*$i/5
                Fill-Ellipse $g $ivory ($cx+18*$scale*[math]::Cos($a)-17*$scale) ($cy+18*$scale*[math]::Sin($a)-17*$scale) (34*$scale) (34*$scale)
            }
            Fill-Ellipse $g $gold ($cx-12*$scale) ($cy-12*$scale) (24*$scale) (24*$scale)
            Draw-Line $g $gold (4*$scale) $cx ($cy+30*$scale) $cx ($cy+47*$scale)
        }
        'bird' {
            Fill-Ellipse $g $ivory ($cx-33*$scale) ($cy-14*$scale) (59*$scale) (35*$scale)
            Fill-Ellipse $g $ivory ($cx+13*$scale) ($cy-28*$scale) (25*$scale) (25*$scale)
            Fill-Polygon $g $gold @([System.Drawing.PointF]::new($cx+37*$scale,$cy-15*$scale),[System.Drawing.PointF]::new($cx+52*$scale,$cy-10*$scale),[System.Drawing.PointF]::new($cx+37*$scale,$cy-5*$scale))
            Fill-Polygon $g (C 250 26 46 70) @([System.Drawing.PointF]::new($cx-12*$scale,$cy-9*$scale),[System.Drawing.PointF]::new($cx+9*$scale,$cy-40*$scale),[System.Drawing.PointF]::new($cx+19*$scale,$cy+3*$scale))
            Draw-Line $g $gold (3*$scale) ($cx-4*$scale) ($cy+18*$scale) ($cx-7*$scale) ($cy+35*$scale)
        }
        'staff' {
            Draw-Line $g $gold (7*$scale) ($cx-4*$scale) ($cy-34*$scale) ($cx-4*$scale) ($cy+48*$scale)
            Draw-Ellipse $g $gold (7*$scale) ($cx-7*$scale) ($cy-53*$scale) (42*$scale) (31*$scale)
        }
        'cross' {
            Draw-Line $g $ivory (10*$scale) $cx ($cy-49*$scale) $cx ($cy+47*$scale)
            Draw-Line $g $gold (10*$scale) ($cx-31*$scale) ($cy-16*$scale) ($cx+31*$scale) ($cy-16*$scale)
        }
        'chalice' {
            Fill-Polygon $g $gold @([System.Drawing.PointF]::new($cx-36*$scale,$cy-31*$scale),[System.Drawing.PointF]::new($cx+36*$scale,$cy-31*$scale),[System.Drawing.PointF]::new($cx+22*$scale,$cy+5*$scale),[System.Drawing.PointF]::new($cx-22*$scale,$cy+5*$scale))
            Draw-Line $g $ivory (7*$scale) $cx ($cy+5*$scale) $cx ($cy+35*$scale)
            Draw-Line $g $gold (7*$scale) ($cx-24*$scale) ($cy+36*$scale) ($cx+24*$scale) ($cy+36*$scale)
        }
        'wing' {
            foreach($side in @(-1,1)) { for($i=0;$i -lt 4;$i++){ Fill-Ellipse $g $ivory ($cx+$side*(12+$i*13)*$scale-18*$scale) ($cy-29*$scale+$i*10*$scale) (30*$scale) (52*$scale) } }
        }
        'lamb' {
            Fill-Ellipse $g $ivory ($cx-35*$scale) ($cy-16*$scale) (56*$scale) (39*$scale)
            Fill-Ellipse $g $ivory ($cx+16*$scale) ($cy-25*$scale) (27*$scale) (27*$scale)
            Draw-Line $g $gold (4*$scale) ($cx-19*$scale) ($cy+20*$scale) ($cx-19*$scale) ($cy+38*$scale)
            Draw-Line $g $gold (4*$scale) ($cx+7*$scale) ($cy+20*$scale) ($cx+7*$scale) ($cy+38*$scale)
        }
        'scroll' {
            Fill-Polygon $g (C 238 233 216 169) @([System.Drawing.PointF]::new($cx-34*$scale,$cy-30*$scale),[System.Drawing.PointF]::new($cx+34*$scale,$cy-30*$scale),[System.Drawing.PointF]::new($cx+28*$scale,$cy+35*$scale),[System.Drawing.PointF]::new($cx-30*$scale,$cy+35*$scale))
            Draw-Line $g $gold (5*$scale) ($cx-36*$scale) ($cy-31*$scale) ($cx+36*$scale) ($cy-31*$scale)
            Draw-Line $g $gold (5*$scale) ($cx-32*$scale) ($cy+35*$scale) ($cx+31*$scale) ($cy+35*$scale)
            Draw-Line $g (C 210 42 53 74) (3*$scale) ($cx-19*$scale) ($cy-5*$scale) ($cx+18*$scale) ($cy-5*$scale)
        }
        'harp' {
            Draw-Line $g $gold (7*$scale) ($cx-28*$scale) ($cy-41*$scale) ($cx-28*$scale) ($cy+43*$scale)
            Draw-Line $g $gold (7*$scale) ($cx-28*$scale) ($cy-40*$scale) ($cx+34*$scale) ($cy-21*$scale)
            Draw-Line $g $gold (7*$scale) ($cx-28*$scale) ($cy+43*$scale) ($cx+28*$scale) ($cy+38*$scale)
            Draw-Line $g $gold (7*$scale) ($cx+34*$scale) ($cy-21*$scale) ($cx+28*$scale) ($cy+38*$scale)
            foreach($i in 0..4) { Draw-Line $g $ivory (2*$scale) ($cx-14*$scale+$i*10*$scale) ($cy-35*$scale+$i*3*$scale) ($cx-14*$scale+$i*10*$scale) ($cy+39*$scale) }
        }
        'trefoil' {
            Fill-Ellipse $g $ivory ($cx-16*$scale) ($cy-45*$scale) (32*$scale) (36*$scale)
            Fill-Ellipse $g $ivory ($cx-39*$scale) ($cy-17*$scale) (34*$scale) (35*$scale)
            Fill-Ellipse $g $ivory ($cx+5*$scale) ($cy-17*$scale) (34*$scale) (35*$scale)
            Draw-Line $g $gold (5*$scale) $cx ($cy+5*$scale) $cx ($cy+46*$scale)
        }
        'lamp' {
            Fill-Ellipse $g $gold ($cx-40*$scale) ($cy+1*$scale) (80*$scale) (32*$scale)
            Fill-Ellipse $g (C 255 39 39 54) ($cx-29*$scale) ($cy+4*$scale) (58*$scale) (15*$scale)
            Fill-Polygon $g $ivory @([System.Drawing.PointF]::new($cx,$cy-49*$scale),[System.Drawing.PointF]::new($cx-14*$scale,$cy-11*$scale),[System.Drawing.PointF]::new($cx+13*$scale,$cy-11*$scale))
            Fill-Ellipse $g (C 245 247 173 79) ($cx-8*$scale) ($cy-23*$scale) (16*$scale) (19*$scale)
        }
        'gift' {
            Fill-Polygon $g (C 240 65 44 60) @([System.Drawing.PointF]::new($cx-34*$scale,$cy-17*$scale),[System.Drawing.PointF]::new($cx+34*$scale,$cy-17*$scale),[System.Drawing.PointF]::new($cx+34*$scale,$cy+38*$scale),[System.Drawing.PointF]::new($cx-34*$scale,$cy+38*$scale))
            Draw-Line $g $gold (7*$scale) $cx ($cy-17*$scale) $cx ($cy+38*$scale)
            Draw-Line $g $gold (7*$scale) ($cx-36*$scale) $cy ($cx+36*$scale) $cy
            Draw-Ellipse $g $ivory (5*$scale) ($cx-30*$scale) ($cy-39*$scale) (28*$scale) (26*$scale)
            Draw-Ellipse $g $ivory (5*$scale) ($cx+2*$scale) ($cy-39*$scale) (28*$scale) (26*$scale)
        }
        'spear' {
            Draw-Line $g $gold (6*$scale) $cx ($cy-19*$scale) $cx ($cy+48*$scale)
            Fill-Polygon $g $ivory @([System.Drawing.PointF]::new($cx,$cy-55*$scale),[System.Drawing.PointF]::new($cx+14*$scale,$cy-16*$scale),[System.Drawing.PointF]::new($cx-14*$scale,$cy-16*$scale))
        }
        default {
            Draw-Line $g $gold (7*$scale) $cx ($cy-48*$scale) $cx ($cy+48*$scale)
            Draw-Line $g $gold (7*$scale) ($cx-40*$scale) $cy ($cx+40*$scale) $cy
            Draw-Line $g $ivory (4*$scale) ($cx-28*$scale) ($cy-28*$scale) ($cx+28*$scale) ($cy+28*$scale)
            Draw-Line $g $ivory (4*$scale) ($cx-28*$scale) ($cy+28*$scale) ($cx+28*$scale) ($cy-28*$scale)
        }
    }
}

$calendar = Get-Content -LiteralPath $DataPath -Raw | ConvertFrom-Json
if (-not $calendar.saints) { throw "No saints found in $DataPath" }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$generated = 0
foreach ($saint in $calendar.saints) {
    $key = [string]$saint.eventKey
    if ($key -notmatch '^[A-Za-z][A-Za-z0-9]*$') { throw "Unsafe saint event key: $key" }
    $out = Join-Path $OutputDirectory "saint-$key-v1.png"
    if (Test-Path -LiteralPath $out) { continue } # Keep individual ImageGen originals.
    $hash = [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($key))
    $seed = [System.BitConverter]::ToInt32($hash, 0) -band 0x7fffffff
    $random = [System.Random]::new($seed)
    $motif = Get-Motif $key ([string]$saint.nameLatin)
    $baseName = $key -creplace '^(Owner)?OurLadyOf|^Sts?',''
    $mark = $baseName -creplace '[^A-Z]',''
    if ($mark.Length -lt 2) { $mark = $baseName.Substring(0, [math]::Min(2, $baseName.Length)).ToUpperInvariant() }
    $mark = $mark.Substring(0, [math]::Min(3, $mark.Length))
    $bitmap = [System.Drawing.Bitmap]::new(512,512,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $g.Clear([System.Drawing.Color]::Transparent)
        $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $path.AddEllipse(21, 27, 470, 460)
        $vignette = [System.Drawing.Drawing2D.PathGradientBrush]::new($path)
        try {
            $vignette.CenterColor = C 250 23 39 69
            $vignette.SurroundColors = [System.Drawing.Color[]]@((C 0 12 24 45))
            $g.FillPath($vignette, $path)
        } finally { $vignette.Dispose(); $path.Dispose() }
        $gold = C 245 224 183 105
        $ivory = C 246 245 233 202
        $outline = C 95 228 181 100
        $variation = $seed % 5
        switch ($variation) {
            0 { Draw-Ellipse $g $outline 3 100 95 312 355; Draw-Ellipse $g (C 70 245 219 165) 2 117 112 278 322 }
            1 { Draw-Line $g $outline 3 134 398 187 119; Draw-Line $g $outline 3 378 398 325 119; Draw-Ellipse $g $outline 3 184 75 144 144 }
            2 { Draw-Ellipse $g $outline 3 79 146 352 279; Draw-Line $g $outline 2 93 330 418 330 }
            3 { Draw-Line $g $outline 3 99 376 191 105; Draw-Line $g $outline 3 413 376 321 105; Draw-Line $g $outline 3 191 105 321 105 }
            4 { Draw-Ellipse $g $outline 3 92 90 328 328; Draw-Line $g (C 65 228 181 100) 2 91 254 421 254 }
        }
        # Key-seeded celestial points form a unique, repeatable constellation.
        $starCount = 4 + ($seed % 4)
        $points = @()
        for ($i=0; $i -lt $starCount; $i++) {
            $angle = (2*[math]::PI*$i/$starCount) + $random.NextDouble()*0.3
            $radius = 171 + $random.Next(0,43)
            $sx = [single](256 + [math]::Cos($angle)*$radius)
            $sy = [single](248 + [math]::Sin($angle)*$radius)
            $points += ,([System.Drawing.PointF]::new($sx,$sy))
            Fill-Ellipse $g (C 230 255 224 160) ($sx-3) ($sy-3) 6 6
        }
        for ($i=0; $i -lt $points.Count-1; $i++) { Draw-Line $g (C 65 232 208 158) 2 $points[$i].X $points[$i].Y $points[$i+1].X $points[$i+1].Y }
        # The foreground emblem is deliberately large enough to read at 60px.
        # The halo and floating constellation echo the cinematic celestial UI.
        Fill-GradientEllipse $g (C 125 211 165 91) (C 0 211 165 91) 77 74 358 344
        Fill-GradientEllipse $g (C 243 16 32 61) (C 0 16 32 61) 110 106 292 280
        Draw-Ellipse $g (C 185 237 201 128) 5 116 110 280 270
        Draw-Ellipse $g (C 75 255 229 170) 2 132 126 248 238
        Draw-Motif $g $motif 256 242 2.05 $gold $ivory
        Draw-String $g $mark 'Georgia' 59 $ivory 145 365 222 81
        Draw-Line $g (C 170 236 194 119) 4 205 440 308 440
        # Fine pigment flecks soften the vectors and make the silhouette less flat.
        for($i=0;$i -lt 250;$i++) {
            $px=$random.Next(102,414)
            $py=$random.Next(100,448)
            $alpha=$random.Next(4,18)
            Fill-Ellipse $g (C $alpha 241 211 157) $px $py 1.1 1.1
        }
        # Render at 2x and downsample for smooth edges and a compact installed app.
        $final = [System.Drawing.Bitmap]::new(256,256,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $finalGraphics = [System.Drawing.Graphics]::FromImage($final)
        try {
            $finalGraphics.Clear([System.Drawing.Color]::Transparent)
            $finalGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $finalGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $finalGraphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $finalGraphics.DrawImage($bitmap, [System.Drawing.Rectangle]::new(0,0,256,256), [System.Drawing.Rectangle]::new(0,0,512,512), [System.Drawing.GraphicsUnit]::Pixel)
            $final.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $finalGraphics.Dispose(); $final.Dispose() }
        $generated++
    } finally { $g.Dispose(); $bitmap.Dispose() }
}
"Generated $generated symbolic PNGs. Existing files preserved."
