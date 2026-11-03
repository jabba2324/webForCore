# Installs IIS 10 with ASP.NET 4.8 (Windows Server) and serves the site in -Path on http://localhost:<Port>/ from an
# application pool in the classic pipeline, .NET CLR v4.0: webForCore's reference (docs/IIS-COMPATIBILITY.md). CI's
# iis-compare job (#78) serves test/webForCore.WebApi.Sample.Iis's site with it.
# Usage: tools/ci/iis-site.ps1 -Path <site folder> [-Port 8090]
param(
    [Parameter(Mandatory = $true)] [string] $Path,
    [int] $Port = 8090
)
$ErrorActionPreference = 'Stop'

# Web-Server brings its default role services (static content, default document, HTTP errors, logging, request
# filtering, static compression); Web-Asp-Net45 brings ASP.NET 4.8 with the ISAPI extension and filter it runs on in the
# classic pipeline.
$result = Install-WindowsFeature -Name Web-Server, Web-Asp-Net45 -IncludeManagementTools
Write-Host "IIS: $($result.ExitCode), restart needed: $($result.RestartNeeded)"
Import-Module WebAdministration
Write-Host "IIS version: $((Get-ItemProperty HKLM:\SOFTWARE\Microsoft\InetStp).VersionString)"
Write-Host ".NET Framework release: $((Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full').Release)"

$Path = (Resolve-Path $Path).Path
# The application pool's identity (IIS_IUSRS) and anonymous authentication's (IUSR) read the site.
icacls $Path /grant "IIS_IUSRS:(OI)(CI)RX" "IUSR:(OI)(CI)RX" /T /Q | Out-Null

$name = 'webForCoreGate'
if (Test-Path "IIS:\Sites\$name") { Remove-Website -Name $name }
if (Test-Path "IIS:\AppPools\$name") { Remove-WebAppPool -Name $name }
New-WebAppPool -Name $name | Out-Null
Set-ItemProperty "IIS:\AppPools\$name" -Name managedRuntimeVersion -Value 'v4.0'
Set-ItemProperty "IIS:\AppPools\$name" -Name managedPipelineMode -Value 'Classic'
New-Website -Name $name -Port $Port -PhysicalPath $Path -ApplicationPool $name | Out-Null
Start-Website -Name $name
Get-Website -Name $name | Format-List name, state, physicalPath, applicationPool
Get-ItemProperty "IIS:\AppPools\$name" | Format-List name, state, managedRuntimeVersion, managedPipelineMode
