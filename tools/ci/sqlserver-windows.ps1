# SQL Server Express on a Windows CI runner (#46), where the Linux job's SQL Server service container can't run:
# 127.0.0.1,14333, login sa, the password of tools/lift/sqlserver.sh, so the tests' connection strings are the same on
# both. For Identity.Test and the IIS gate's data test. CI use only.
$ErrorActionPreference = 'Stop'
$password = 'webForCore-Test1!'
$port = 14333

$ini = Join-Path $env:RUNNER_TEMP 'sqlserver.ini'
@"
[OPTIONS]
ACTION="Install"
FEATURES=SQLENGINE
INSTANCEID="SQLEXPRESS"
INSTANCENAME="SQLEXPRESS"
SECURITYMODE="SQL"
SAPWD="$password"
SQLSYSADMINACCOUNTS="BUILTIN\Administrators"
TCPENABLED="1"
UPDATEENABLED="False"
"@ | Set-Content -Path $ini -Encoding ASCII
choco install sql-server-express -y --no-progress --params="'/ConfigurationFile:$ini'"
if ($LASTEXITCODE -ne 0) { throw "choco install sql-server-express failed ($LASTEXITCODE)" }

# A fixed port instead of a dynamic one.
$id = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL').SQLEXPRESS
$tcp = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$id\MSSQLServer\SuperSocketNetLib\Tcp"
Set-ItemProperty -Path $tcp -Name Enabled -Value 1
Set-ItemProperty -Path "$tcp\IPAll" -Name TcpDynamicPorts -Value ''
Set-ItemProperty -Path "$tcp\IPAll" -Name TcpPort -Value "$port"
Restart-Service -Name 'MSSQL$SQLEXPRESS' -Force

for ($i = 0; $i -lt 60; $i++) {
    try {
        $client = [System.Net.Sockets.TcpClient]::new('127.0.0.1', $port)
        $client.Dispose()
        Write-Host "SQL Server ready on 127.0.0.1,$port"
        exit 0
    } catch {
        Start-Sleep -Seconds 2
    }
}
throw "SQL Server did not listen on port $port"
