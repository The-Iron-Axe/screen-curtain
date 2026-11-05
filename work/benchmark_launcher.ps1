param([Parameter(Mandatory=$true)][string]$Executable,[Parameter(Mandatory=$true)][string]$CacheRoot,
      [Parameter(Mandatory=$true)][string]$Tag,[switch]$NoCompact)
$ErrorActionPreference='Stop'
$exePath=[System.IO.Path]::GetFullPath($Executable)
$testRoot=[System.IO.Path]::GetFullPath($CacheRoot)
$report=[System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ('ExtremeLab/'+$Tag+'-numbering.txt')))
$env:SCREENCURTAIN_TEST_CACHE_ROOT=$testRoot
if($NoCompact) { $env:SCREENCURTAIN_TEST_NO_COMPACT='1' } else { Remove-Item Env:SCREENCURTAIN_TEST_NO_COMPACT -ErrorAction SilentlyContinue }
$stopwatch=[System.Diagnostics.Stopwatch]::StartNew()
$launcher=Start-Process -FilePath $exePath -ArgumentList @('--self-test-numbering',('"'+$report+'"')) -PassThru -WindowStyle Hidden
$maxTotalWorking=0; $maxTotalPrivate=0; $samples=0
while(-not $launcher.HasExited) {
    if($stopwatch.Elapsed.TotalSeconds -gt 180) { throw "Timed out waiting for $Tag (PID $($launcher.Id))." }
    $processes=@(Get-CimInstance Win32_Process -Filter "ProcessId=$($launcher.Id) OR ParentProcessId=$($launcher.Id)")
    $sumWorking=0; $sumPrivate=0
    foreach($item in $processes) {
        $p=Get-Process -Id $item.ProcessId -ErrorAction SilentlyContinue
        if($p) { $sumWorking+=$p.WorkingSet64; $sumPrivate+=$p.PrivateMemorySize64 }
    }
    $maxTotalWorking=[math]::Max($maxTotalWorking,$sumWorking); $maxTotalPrivate=[math]::Max($maxTotalPrivate,$sumPrivate); $samples++
    Start-Sleep -Milliseconds 250
    $launcher.Refresh()
}
$stopwatch.Stop()
$result=[pscustomobject]@{Tag=$Tag;ExitCode=$launcher.ExitCode;Seconds=[math]::Round($stopwatch.Elapsed.TotalSeconds,2);
    PeakWorkingMiB=[math]::Round($maxTotalWorking/1MB,2);PeakPrivateCommitMiB=[math]::Round($maxTotalPrivate/1MB,2);
    Samples=$samples;ExeMiB=[math]::Round((Get-Item -LiteralPath $exePath).Length/1MB,2);CacheRoot=$testRoot}
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot ('ExtremeLab/'+$Tag+'-measurement.json')) -Encoding utf8
$result | ConvertTo-Json
if($launcher.ExitCode -ne 0) {
    if(Test-Path -LiteralPath ($report+'.launcher-error.txt')) { Get-Content -LiteralPath ($report+'.launcher-error.txt') }
    if(Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Tail 4 }
    exit 1
}
