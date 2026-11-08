param(
    [Parameter(Mandatory)][string]$Archive,
    [string]$Feed,
    [Parameter(Mandatory)][string]$TestRoot,
    [string]$ExpectedVersion = '0.4.1',
    [switch]$RejectCorruptPackage,
    [switch]$UseGitHub
)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $TestRoot) { throw 'Use a fresh, isolated test directory' }
New-Item -ItemType Directory -Path $TestRoot | Out-Null
$testDirectory = (Resolve-Path -LiteralPath $TestRoot).Path
$appDirectory = Join-Path $testDirectory 'app'
$dataDirectory = Join-Path $testDirectory 'data'
Expand-Archive -LiteralPath $Archive -DestinationPath $appDirectory
New-Item -ItemType Directory -Path $dataDirectory | Out-Null
$watcher = [System.IO.FileSystemWatcher]::new($dataDirectory, 'update-result.json')
$watcher.EnableRaisingEvents = $true
try {
    $mode = if ($RejectCorruptPackage) { '--verify-update-rejection' } else { '--verify-update' }
    $source = if ($UseGitHub) { '--verify-update-github' } else { '{0} "{1}"' -f $mode, (Resolve-Path -LiteralPath $Feed).Path }
    $arguments = '--demo --data-dir "{0}" {1} --expect-version {2}' -f $dataDirectory, $source, $ExpectedVersion
    $process = Start-Process (Join-Path $appDirectory 'Lateraroot.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $resultPath = Join-Path $dataDirectory 'update-result.json'
    $deadline = [DateTime]::UtcNow.AddMinutes(3)
    while (!(Test-Path -LiteralPath $resultPath) -and [DateTime]::UtcNow -lt $deadline) {
        $null = $watcher.WaitForChanged([System.IO.WatcherChangeTypes]::Created -bor [System.IO.WatcherChangeTypes]::Renamed, 30000)
    }
    if (!(Test-Path -LiteralPath $resultPath)) { throw "Update test timed out; inspect $testDirectory" }
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if (!$result.Passed) { throw ($result | ConvertTo-Json -Depth 5) }
    $result | ConvertTo-Json -Depth 5
    if (!$process.WaitForExit(10000)) { throw 'Original process did not exit' }
    if ($process.ExitCode -ne 0) { throw "Original process exited with $($process.ExitCode)" }
} finally { $watcher.Dispose() }
