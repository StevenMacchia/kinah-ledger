# The accuracy bench: every recording in the bench manifest counted by this build and scored against the baseline.
# See README.md beside this file. Exit code 1 when any run regressed.
param(
    [string]$Manifest = "",     # kinah-bench\bench.json; found beside the repo's parent folder when not given
    [string]$Exe = "",          # the built tracker; the Release build in this checkout when not given
    [string]$Only = "",         # one run's name
    [switch]$Fresh,             # read the frames again (a reader change)
    [switch]$Record             # keep this build's scores as the new baseline
)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Resolve-Path (Join-Path $here "..\..\..")

if ($Manifest -eq "") {
    $dir = $repo.Path
    while ($dir -and -not (Test-Path (Join-Path $dir "kinah-bench\bench.json"))) { $dir = Split-Path -Parent $dir }
    if (-not $dir) { throw "no kinah-bench\bench.json beside this repo or any parent; pass -Manifest" }
    $Manifest = Join-Path $dir "kinah-bench\bench.json"
}
$bench = Split-Path -Parent (Resolve-Path $Manifest)
if ($Exe -eq "") {
    $Exe = Get-ChildItem (Join-Path $repo "tracker\KinahLedger.Tracker\bin\Release") -Recurse -Filter KinahLedger.exe |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (-not $Exe) { throw "no Release build; run dotnet build -c Release in tracker\KinahLedger.Tracker" }
}
$baseline = Join-Path $here "baseline.json"
$runs = (Get-Content $Manifest -Raw | ConvertFrom-Json).runs
if ($Only -ne "") { $runs = $runs | Where-Object { $_.name -eq $Only } }
if (-not $runs) { throw "no run named $Only in $Manifest" }

Write-Host "bench: $Exe"
$failed = @(); $results = @()
foreach ($r in $runs) {
    $cli = @("bench-replay", (Join-Path $bench $r.frames), "--start", $r.start, "--truth", (Join-Path $bench $r.truth),
             "--baseline", $baseline, "--run", $r.name)
    if ($r.reads) { $cli += @("--reads", (Join-Path $bench $r.reads)) }
    if ($r.offset) { $cli += @("--offset", "$($r.offset)") }
    if ($r.chat) { $cli += @("--chat", $r.chat) }
    if ($Fresh) { $cli += "--fresh" }
    if ($Record) { $cli += "--record" }
    Write-Host ""
    Write-Host "== $($r.name)"
    # the tracker is a windowed exe: PowerShell would not wait for it, so it runs with an explicit wait and its output captured
    $quoted = $cli | ForEach-Object { if ("$_" -match '\s') { '"' + $_ + '"' } else { "$_" } }
    $out = [System.IO.Path]::GetTempFileName()
    $p = Start-Process -FilePath $Exe -ArgumentList $quoted -NoNewWindow -Wait -PassThru -RedirectStandardOutput $out
    Get-Content $out | Where-Object { $_ -notmatch '^  read \d+ frames' } | ForEach-Object { Write-Host $_ }
    Remove-Item $out -ErrorAction SilentlyContinue
    $code = $p.ExitCode
    $results += "$($r.name): " + $(if ($code -eq 0) { "ok" } else { "FAIL ($code)" })
    if ($code -ne 0) { $failed += $r.name }
}
Write-Host ""
Write-Host ("bench: " + ($results -join " | "))
if ($failed.Count -gt 0) { exit 1 }
exit 0
