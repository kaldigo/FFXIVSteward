[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/preview'),
    [string]$Renderer
)
$ErrorActionPreference = 'Stop'
$null = New-Item -ItemType Directory -Force $OutputDirectory
if (!$Renderer) {
    $tools = Join-Path $PSScriptRoot '../artifacts/preview-tools/0.1.0-beta.3'
    $Renderer = Join-Path $tools 'glasswork-preview.exe'
    if (!(Test-Path $Renderer)) {
        dotnet tool install Glasswork.Preview --version 0.1.0-beta.3 --tool-path $tools
        if ($LASTEXITCODE) { throw 'Could not install Glasswork.Preview' }
    }
}
dotnet build (Join-Path $PSScriptRoot 'Steward.Preview.csproj') -c Release --nologo
if ($LASTEXITCODE) { throw 'Could not build Steward preview' }
$assembly = Join-Path $PSScriptRoot 'bin/Release/net10.0-windows/Steward.Preview.dll'
# A quiet backdrop makes the translucent controls legible without a game capture.
Add-Type -AssemblyName System.Drawing
# Preview has no game texture provider. Sample the packaged icon for its draw list.
$sourceIcon = [Drawing.Bitmap]::new((Resolve-Path (Join-Path $PSScriptRoot '../assets/mark.png')).Path)
$smallIcon = [Drawing.Bitmap]::new($sourceIcon,30,30)
try {
    $rgba = foreach ($y in 0..29) { foreach ($x in 0..29) {
        $pixel = $smallIcon.GetPixel($x,$y)
        $pixel.R, $pixel.G, $pixel.B, $pixel.A
    }}
    [IO.File]::WriteAllBytes((Join-Path (Split-Path $assembly) 'icon.rgba'),[byte[]]$rgba)
} finally { $smallIcon.Dispose(); $sourceIcon.Dispose() }
$backdrop = Join-Path $OutputDirectory 'backdrop.png'
$bitmap = [Drawing.Bitmap]::new(640, 480)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$rect = [Drawing.Rectangle]::new(0, 0, 640, 480)
$brush = [Drawing.Drawing2D.LinearGradientBrush]::new($rect, [Drawing.Color]::FromArgb(22, 45, 63), [Drawing.Color]::FromArgb(53, 38, 61), 35.0)
try {
    $graphics.FillRectangle($brush, $rect)
    $bitmap.Save($backdrop, [Drawing.Imaging.ImageFormat]::Png)
} finally { $brush.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
foreach ($scene in 'steward-activity', 'steward-compact', 'steward-filters', 'steward-review', 'steward-navigation', 'steward-sales', 'steward-back', 'steward-inventory') {
    & $Renderer --assembly $assembly --scene $scene --out (Join-Path $OutputDirectory "$scene.png") --backdrop $backdrop --warp --deterministic
    if ($LASTEXITCODE) { throw "Preview failed for $scene" }
}

foreach ($width in 'normal','compact') {
    foreach ($page in 'Activity','Inventory','Market','Collection','Setup','Sales','Glamour','Storage','Items','Connections','Review','Strategies','Disposal','Readiness') {
        $env:STEWARD_PREVIEW_WIDTH=$width
        $env:STEWARD_PREVIEW_PAGE=$page
        & $Renderer --assembly $assembly --scene steward-page --out (Join-Path $OutputDirectory "$page-$width.png") --backdrop $backdrop --warp --deterministic
        if ($LASTEXITCODE) { throw "Preview failed for $page at $width width" }
    }
}
