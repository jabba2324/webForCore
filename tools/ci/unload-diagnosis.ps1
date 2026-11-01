# Temporary (#46): why an old PseudoAppDomain is not collected on Windows. Runs the Web API sample as its gate does,
# restarts it (Web.config touched), and when start 1 has not unloaded, dumps the process and asks SOS what roots the
# old domain. Output in $env:RUNNER_TEMP/unload.
$ErrorActionPreference = 'Stop'
$out = Join-Path $env:RUNNER_TEMP 'unload'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$sample = Join-Path $PWD 'test/webForCore.WebApi.Sample'
$url = 'http://127.0.0.1:5099'

$env:GATE_RECYCLE_CHECK = '2'
$stdout = Join-Path $out 'sample.log'
$stderr = Join-Path $out 'sample.err.log'
$process = Start-Process -FilePath 'dotnet' -ArgumentList "bin_dotnet/webForCore.WebApi.Sample.dll --urls $url" `
    -WorkingDirectory $sample -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -NoNewWindow
Write-Host "Sample started, pid $($process.Id)"
function Text { (Get-Content -Raw -ErrorAction SilentlyContinue $stdout) + (Get-Content -Raw -ErrorAction SilentlyContinue $stderr) }
function Get($path, $accept = 'application/json') {
    try { Invoke-WebRequest -Uri "$url$path" -Headers @{ Accept = $accept } -UseBasicParsing -TimeoutSec 30 | Out-Null; $true } catch { $false }
}

try {
    for ($i = 0; $i -lt 120 -and -not (Get '/api/values'); $i++) { Start-Sleep -Milliseconds 500 }
    foreach ($path in '/api/products', '/api/products/1', '/api/status/item/7', '/owin/api/products') {
        foreach ($accept in 'application/json', 'application/xml') { Get $path $accept | Out-Null }
    }
    (Get-Item (Join-Path $sample 'Web.config')).LastWriteTimeUtc = [DateTime]::UtcNow
    for ($i = 0; $i -lt 150 -and -not ((Text) -match '\(start 1\) (unloaded|has not unloaded)'); $i++) {
        Start-Sleep -Milliseconds 200
        if ($i % 10 -eq 0) { Get '/api/values' | Out-Null }
    }
    if ((Text) -match '\(start 1\) unloaded\.') {
        Write-Host 'Start 1 unloaded: nothing to diagnose.'
    }
    else {
        Write-Host 'Start 1 has not unloaded: dumping the process.'
        $dump = Join-Path $out 'sample.dmp'
        dotnet-dump collect -p $process.Id --type Full -o $dump
        function Sos([string[]]$commands, $file) {
            $arguments = @('analyze', $dump) + ($commands | ForEach-Object { '-c', $_ }) + @('-c', 'exit')
            & dotnet-dump @arguments *>&1 | Out-File -FilePath (Join-Path $out $file) -Encoding utf8
        }
        Sos @('dumpheap -stat -type System.Web.Hosting.PseudoAppDomain', 'dumpheap -stat -type System.Web.HttpRuntime') 'heap.txt'
        $roots = @()
        foreach ($type in 'System.Web.Hosting.PseudoAppDomain', 'System.Web.HttpRuntime') {
            $short = & dotnet-dump analyze $dump -c "dumpheap -short -type $type" -c exit
            foreach ($line in $short) {
                $address = $line.Trim()
                if ($address -match '^(0x)?[0-9a-fA-F]{8,16}$') { $roots += "dumpobj $address"; $roots += "gcroot -all $address" }
            }
        }
        if ($roots.Count -gt 0) { Sos $roots 'gcroot.txt' }
        Remove-Item $dump
        Get-ChildItem $out | ForEach-Object { Write-Host "::group::$($_.Name)"; Get-Content $_.FullName | Write-Host; Write-Host '::endgroup::' }
    }
}
finally {
    if (-not $process.HasExited) { $process.Kill($true) }
    Start-Sleep -Seconds 1
    Write-Host '::group::sample log'; Write-Host (Text); Write-Host '::endgroup::'
}
