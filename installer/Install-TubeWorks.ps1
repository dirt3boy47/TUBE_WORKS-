<#
.SYNOPSIS
  Install and register the TubeWorks Designer add-in for SOLIDWORKS (per machine).
.DESCRIPTION
  Copies the build output to the install folder and registers the COM classes with the 64-bit RegAsm
  (/codebase). Registration also writes HKLM\SOFTWARE\SolidWorks\Addins\{GUID} so that SOLIDWORKS lists the
  add-in, and HKCU\Software\SolidWorks\AddInsStartup\{GUID} so that it loads at start-up for the user who runs this script.
  Run in an elevated (Administrator) PowerShell. Close SOLIDWORKS first.
.PARAMETER Source
  Folder that contains TubeWorks.AddIn.dll (default: the Release build output).
.PARAMETER Target
  Install folder (default: C:\Program Files\TubeWorks Designer).
#>
param(
    [string]$Source = (Join-Path $PSScriptRoot "..\src\TubeWorks.AddIn\bin\Release\net48"),
    [string]$Target = (Join-Path $env:ProgramFiles "TubeWorks Designer")
)
$ErrorActionPreference = "Stop"

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw "Run this script as Administrator." }
if (Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue) { throw "Close SOLIDWORKS first." }

$dll = Join-Path $Source "TubeWorks.AddIn.dll"
if (-not (Test-Path $dll)) { throw "Not found: $dll. Build the solution in Release first (see docs/BUILD-AND-INSTALL.md)." }

$regasm = Join-Path $env:windir "Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
if (-not (Test-Path $regasm)) { throw "64-bit RegAsm not found: $regasm (.NET Framework 4.8 is required)." }

New-Item -ItemType Directory -Force -Path $Target | Out-Null
Copy-Item -Path (Join-Path $Source "*") -Destination $Target -Recurse -Force

$installed = Join-Path $Target "TubeWorks.AddIn.dll"
& $regasm /codebase $installed
if ($LASTEXITCODE -ne 0) { throw "RegAsm failed with exit code $LASTEXITCODE." }

Write-Host "TubeWorks Designer installed to $Target and registered."
Write-Host "Start SOLIDWORKS, open a part, and use the 'Tube Design' tab. (Tools > Add-Ins lists 'TubeWorks Designer'.)"
