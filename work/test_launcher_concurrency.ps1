param([Parameter(Mandatory=$true)][string]$Executable,[Parameter(Mandatory=$true)][string]$CacheRoot)
$ErrorActionPreference='Stop'
$testRoot=[System.IO.Path]::GetFullPath($CacheRoot)
$labRoot=[System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'ExtremeLab')).TrimEnd('\')+'\'
if(-not $testRoot.StartsWith($labRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Concurrency test cache must be inside the test workspace.' }
if(Test-Path -LiteralPath $testRoot) { throw 'Use a fresh concurrency test cache.' }
New-Item -ItemType Directory -Path $testRoot | Out-Null
$env:SCREENCURTAIN_TEST_CACHE_ROOT=$testRoot; $env:SCREENCURTAIN_TEST_NO_COMPACT='1'
$exePath=[System.IO.Path]::GetFullPath($Executable)
$report1=Join-Path $labRoot 'concurrency-first.txt'; $report2=Join-Path $labRoot 'concurrency-second.txt'
$first=Start-Process -FilePath $exePath -ArgumentList @('--self-test-launcher-only',('"'+$report1+'"')) -PassThru -WindowStyle Hidden
$second=Start-Process -FilePath $exePath -ArgumentList @('--self-test-launcher-only',('"'+$report2+'"')) -PassThru -WindowStyle Hidden
if(-not $first.WaitForExit(120000) -or -not $second.WaitForExit(120000)) { throw 'Concurrent launch did not finish.' }
if($first.ExitCode -ne 0 -or $second.ExitCode -ne 0) { throw 'One concurrent launcher failed.' }
if((Get-Content -LiteralPath $report1 -Raw) -notmatch '^PASS' -or (Get-Content -LiteralPath $report2 -Raw) -notmatch '^PASS') { throw 'Both launchers must validate the cache.' }
$directories=@(Get-ChildItem -LiteralPath $testRoot -Directory -Force)
if($directories.Count -ne 1 -or -not (Test-Path -LiteralPath (Join-Path $directories[0].FullName '.ready'))) { throw 'Concurrent starts did not leave exactly one valid cache.' }
'PASS: simultaneous first starts share one verified cache and both complete.'
