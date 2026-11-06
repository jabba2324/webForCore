# Redis on a Windows CI runner, where the Linux job's Redis service container can't run: Memurai Developer, a Redis
# 7 compatible server for Windows (Chocolatey's memurai-developer), on 127.0.0.1:16379, the address tools/ci/redis.sh
# uses, so the SignalR scale-out tests (IIS.Gate.Tests' ScaleoutTests) connect the same way on both. CI use only.
$ErrorActionPreference = 'Stop'
$port = 16379

choco install memurai-developer -y --no-progress
if ($LASTEXITCODE -ne 0) { throw "choco install memurai-developer failed ($LASTEXITCODE)" }

# The installer runs Memurai as a service on 6379, from memurai.conf: move it to the tests' port.
$dir = Join-Path $env:ProgramFiles 'Memurai'
$conf = Join-Path $dir 'memurai.conf'
if (-not (Test-Path $conf)) { throw "no $conf" }
(Get-Content $conf) -replace '^port\s+\d+', "port $port" | Set-Content -Path $conf -Encoding ASCII
Restart-Service -Name 'Memurai' -Force

for ($i = 0; $i -lt 60; $i++) {
    try {
        $client = [System.Net.Sockets.TcpClient]::new('127.0.0.1', $port)
        $client.Dispose()
        Write-Host "Redis (Memurai) ready on 127.0.0.1:$port"
        exit 0
    } catch {
        Start-Sleep -Seconds 2
    }
}
throw "Memurai did not listen on port $port"
