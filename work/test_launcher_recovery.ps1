param([Parameter(Mandatory=$true)][string]$Executable,[Parameter(Mandatory=$true)][string]$CacheRoot)
$ErrorActionPreference='Stop'
$exePath=[System.IO.Path]::GetFullPath($Executable)
$testRoot=[System.IO.Path]::GetFullPath($CacheRoot)
$labRoot=[System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'ExtremeLab')).TrimEnd('\')+'\'
if (-not $testRoot.StartsWith($labRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Recovery test cache must be inside the test workspace.' }
if(Test-Path -LiteralPath $testRoot) { throw 'Use a fresh recovery test cache.' }
New-Item -ItemType Directory -Path $testRoot | Out-Null
$env:SCREENCURTAIN_TEST_CACHE_ROOT=$testRoot; $env:SCREENCURTAIN_TEST_NO_COMPACT='1'
$report=[System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'ExtremeLab/interrupted-numbering.txt'))
$launched=Start-Process -FilePath $exePath -ArgumentList @('--self-test-numbering',('"'+$report+'"')) -PassThru -WindowStyle Hidden
$deadline=[DateTime]::UtcNow.AddSeconds(30); $stage=$null
while([DateTime]::UtcNow -lt $deadline -and -not $launched.HasExited) {
    $stage=Get-ChildItem -LiteralPath $testRoot -Directory -Force -Filter '.screen-curtain-stage-*' | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName '.stage-owner') } | Select-Object -First 1
    if($stage) { break }
    Start-Sleep -Milliseconds 100; $launched.Refresh()
}
if(-not $stage) { throw 'Did not observe a fresh extraction before the test ended.' }
Stop-Process -Id $launched.Id -Force
Start-Sleep -Seconds 2
& (Join-Path $PSScriptRoot 'benchmark_launcher.ps1') -Executable $exePath -CacheRoot $testRoot -Tag recovered-after-interruption -NoCompact
$remaining=@(Get-ChildItem -LiteralPath $testRoot -Directory -Force -Filter '.screen-curtain-stage-*')
if($remaining.Count -ne 0) { throw 'An abandoned owned staging directory was not cleaned.' }
if(-not (Select-String -LiteralPath (Join-Path $PSScriptRoot 'ExtremeLab/recovered-after-interruption-numbering.txt') -SimpleMatch 'RESULT: 13 numbering checks passed')) { throw 'Recovery did not reach a fully working application.' }
'PASS: interrupted extraction was cleaned and the next launch completed successfully.'
