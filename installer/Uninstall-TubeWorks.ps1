<#
.SYNOPSIS
  Unregister and remove the TubeWorks Designer add-in. Run as Administrator with SOLIDWORKS closed.
#>
param([string]$Target = (Join-Path $env:ProgramFiles "TubeWorks Designer"))
$ErrorActionPreference = "Stop"
if (Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue) { throw "Close SOLIDWORKS first." }
$regasm = Join-Path $env:windir "Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
$installed = Join-Path $Target "TubeWorks.AddIn.dll"
if (Test-Path $installed) {
    & $regasm /u $installed
    if ($LASTEXITCODE -ne 0) { throw "RegAsm /u failed with exit code $LASTEXITCODE." }
}
Remove-Item -Recurse -Force $Target -ErrorAction SilentlyContinue
Write-Host "TubeWorks Designer removed. User settings in %APPDATA%\TubeWorks were kept."
