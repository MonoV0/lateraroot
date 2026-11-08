param([string]$CacheDirectory = (Join-Path $PSScriptRoot '../artifacts/icon-cache'))
$ErrorActionPreference = 'Stop'
$source = 'https://cdn-uicons.flaticon.com/4.0.0/uicons-thin-rounded'
New-Item -ItemType Directory -Force -Path $CacheDirectory | Out-Null
$files = @(
    @('css/uicons-thin-rounded.css', 'thin-rounded-4.0.0.css', '2B447922F20882D99DBF825109D1A29894A3A1440D0F9A20FC7906F2D666AEC6'),
    @('webfonts/uicons-thin-rounded.eot', 'thin-rounded-4.0.0.eot', 'D103D92C32D46AE310D7803BC6D38C95B77086351921E2B13078BF3B581268B5')
)
foreach ($file in $files) {
    $path = Join-Path $CacheDirectory $file[1]
    if (!(Test-Path -LiteralPath $path)) { Invoke-WebRequest "$source/$($file[0])" -OutFile $path -UseBasicParsing }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file[2]) { throw "Official icon source checksum mismatch: $path" }
}
# EOT is an uncompressed wrapper in this pinned official release; extract its unchanged TTF payload.
$eot = [IO.File]::ReadAllBytes((Join-Path $CacheDirectory 'thin-rounded-4.0.0.eot'))
$size = [BitConverter]::ToUInt32($eot, 4)
if ([BitConverter]::ToUInt32($eot, 12) -ne 0 -or $size -ge $eot.Length) { throw 'Unexpected EOT format' }
$ttf = New-Object byte[] $size
[Array]::Copy($eot, $eot.Length - $size, $ttf, 0, $size)
if ($ttf[0] -ne 0 -or $ttf[1] -ne 1 -or $ttf[2] -ne 0 -or $ttf[3] -ne 0) { throw 'Unexpected TrueType signature' }
$fontPath = [IO.Path]::GetFullPath((Join-Path $CacheDirectory 'thin-rounded-4.0.0.ttf'))
[IO.File]::WriteAllBytes($fontPath, $ttf)
Add-Type -AssemblyName PresentationCore
$font = New-Object System.Windows.Media.GlyphTypeface ([Uri]$fontPath)
$css = [IO.File]::ReadAllText((Join-Path $CacheDirectory 'thin-rounded-4.0.0.css'))
$icons = [ordered]@{
    ArrowOut='arrow-up-right'; ArrowIn='arrow-down-left'; CloseIcon='cross'; MinusIcon='minus'; GripIcon='grip-dots'
    SettingsIcon='settings'; HideIcon='eye-crossed'; SearchIcon='search'; ReloadIcon='refresh'
    LockIcon='lock'; AddIcon='plus'; UpIcon='arrow-small-up'; DownIcon='arrow-small-down'
}
$culture = [Globalization.CultureInfo]::InvariantCulture
$output = New-Object Text.StringBuilder
$null = $output.AppendLine('<!-- Compiled UI artwork: Uicons by Flaticon; separate Flaticon license, not MIT. Generated; do not redistribute as an icon pack. -->')
$null = $output.AppendLine('<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">')
foreach ($entry in $icons.GetEnumerator()) {
    $match = [regex]::Match($css, '\.fi-tr-' + [regex]::Escape($entry.Value) + ':before\s*\{\s*content:\s*"\\([0-9a-f]+)"')
    if (!$match.Success) { throw "Official glyph not found: $($entry.Value)" }
    $code = [Convert]::ToInt32($match.Groups[1].Value, 16)
    $geometry = $font.GetGlyphOutline($font.CharacterToGlyphMap[$code], 1, 1)
    $bounds = $geometry.Bounds
    $scale = 24 / [Math]::Max($bounds.Width, $bounds.Height)
    $x = (24 - $bounds.Width * $scale) / 2 - $bounds.X * $scale
    $y = (24 - $bounds.Height * $scale) / 2 - $bounds.Y * $scale
    $matrix = '{0},0,0,{0},{1},{2}' -f $scale.ToString('R', $culture), $x.ToString('R', $culture), $y.ToString('R', $culture)
    $data = [Security.SecurityElement]::Escape($geometry.ToString($culture))
    $null = $output.AppendLine(('  <GeometryGroup x:Key="{0}" Transform="{1}"><Geometry>{2}</Geometry></GeometryGroup>' -f $entry.Key, $matrix, $data))
}
$null = $output.AppendLine('</ResourceDictionary>')
$destination = Join-Path $PSScriptRoot '../src/Lateraroot/Generated'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
[IO.File]::WriteAllText((Join-Path $destination 'FlaticonIcons.xaml'), $output.ToString(), (New-Object Text.UTF8Encoding $false))
Write-Output "Prepared $($icons.Count) Flaticon Thin Rounded 4.0.0 glyphs for compilation. Original font and generated icon source are excluded from Git."
