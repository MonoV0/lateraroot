param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$Version = '',
    [string]$Vpk = 'vpk'
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = ([xml](Get-Content (Join-Path $PSScriptRoot '../src/Lateraroot/Lateraroot.csproj'))).Project.PropertyGroup.Version }
# Distinct from %LOCALAPPDATA%\Lateraroot\Data: updater cleanup must never own the user's Vault.
& $Vpk pack --packId LaterarootApp --packTitle Lateraroot --packAuthors MonoV0 --packVersion $Version --packDir $PublishDirectory --mainExe Lateraroot.exe --outputDir $OutputDirectory --runtime win-x64 --releaseNotes (Join-Path $PSScriptRoot '../docs/RELEASE_NOTES.md') --noInst --delta None --yes --skip-updates
if ($LASTEXITCODE -ne 0) { throw 'Velopack packaging failed' }
$portable = Get-ChildItem $OutputDirectory -Filter '*Portable.zip' | Select-Object -First 1
if (!$portable) { throw 'Portable ZIP not produced' }
$archive = Join-Path $OutputDirectory "lateraroot-v$Version-win-x64.zip"
Move-Item -LiteralPath $portable.FullName -Destination $archive
$assetsPath = Join-Path $OutputDirectory 'assets.win.json'
$assets = @(Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json)
foreach ($asset in $assets) { if ($asset.RelativeFileName -eq $portable.Name) { $asset.RelativeFileName = Split-Path $archive -Leaf } }
ConvertTo-Json -InputObject $assets -Compress | Set-Content -LiteralPath $assetsPath -Encoding utf8
$hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $archive -Leaf)" | Set-Content "$archive.sha256" -Encoding utf8
