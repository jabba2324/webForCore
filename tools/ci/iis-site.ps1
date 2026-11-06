# Installs IIS 10 with ASP.NET 4.8 (Windows Server) and serves the site in -Path on http://localhost:<Port>/ as site
# -Name, from an application pool of its own (.NET CLR v4.0) in the -Pipeline mode (Integrated or Classic). CI's
# iis-compare job (#78) serves test/webForCore.WebApi.Sample.Iis's site with it in both modes. With -Application <name>,
# -Path is the site's application /<name> instead, and the site's root an empty folder beside it, <Path>-root (#190).
# Usage: tools/ci/iis-site.ps1 -Path <site folder> -Name <site> -Port <port> -Pipeline Integrated|Classic [-Application <name>]
# Windows PowerShell (powershell, not pwsh): PowerShell 7 loads WebAdministration through a remoting session, without
# its IIS: drive.
param(
    [Parameter(Mandatory = $true)] [string] $Path,
    [Parameter(Mandatory = $true)] [string] $Name,
    [Parameter(Mandatory = $true)] [int] $Port,
    [Parameter(Mandatory = $true)] [ValidateSet('Integrated', 'Classic')] [string] $Pipeline,
    [string] $Application = ''
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

$name = $Name
if (Test-Path "IIS:\Sites\$name") { Remove-Website -Name $name }
if (Test-Path "IIS:\AppPools\$name") { Remove-WebAppPool -Name $name }
New-WebAppPool -Name $name | Out-Null
Set-ItemProperty "IIS:\AppPools\$name" -Name managedRuntimeVersion -Value 'v4.0'
Set-ItemProperty "IIS:\AppPools\$name" -Name managedPipelineMode -Value $Pipeline
if ($Application) {
    $root = "$Path-root"
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    icacls $root /grant "IIS_IUSRS:(OI)(CI)RX" "IUSR:(OI)(CI)RX" /T /Q | Out-Null
    New-Website -Name $name -Port $Port -PhysicalPath $root -ApplicationPool $name | Out-Null
    New-WebApplication -Site $name -Name $Application -PhysicalPath $Path -ApplicationPool $name | Out-Null
} else {
    New-Website -Name $name -Port $Port -PhysicalPath $Path -ApplicationPool $name | Out-Null
}
Start-Website -Name $name
Get-Website -Name $name | Format-List name, state, physicalPath, applicationPool
if ($Application) { Get-WebApplication -Site $name -Name $Application | Format-List path, physicalPath, applicationPool }
Get-ItemProperty "IIS:\AppPools\$name" | Format-List name, state, managedRuntimeVersion, managedPipelineMode
