<#
.SYNOPSIS
  Developer registration: register the Debug build in place (no copy). Run as Administrator.
  Use -Unregister to remove the registration.
#>
param([string]$Configuration = "Debug", [switch]$Unregister)
$ErrorActionPreference = "Stop"
$dll = Resolve-Path (Join-Path $PSScriptRoot "..\src\TubeWorks.AddIn\bin\$Configuration\net48\TubeWorks.AddIn.dll")
$regasm = Join-Path $env:windir "Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
if ($Unregister) { & $regasm /u $dll } else { & $regasm /codebase $dll }
exit $LASTEXITCODE
