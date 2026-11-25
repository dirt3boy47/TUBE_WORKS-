<#
.SYNOPSIS
  Build the Release add-in and the setup program on Windows. Needs .NET SDK 8 and NSIS 3 (makensis.exe on PATH or in Program Files).
  By default it compiles against YOUR SOLIDWORKS installation (api\redist). Use -InteropDir to choose another folder.
#>
param([string]$InteropDir = (Join-Path ${env:ProgramW6432} "SOLIDWORKS Corp\SOLIDWORKS\api\redist"))
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
[xml]$props = Get-Content (Join-Path $root "Directory.Build.props")
$version = $props.Project.PropertyGroup.TubeWorksVersion
$work = Join-Path $root "installer\work"; $stage = Join-Path $work "stage"; $out = Join-Path $root "installer\out"
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path (Join-Path $stage "Documentation"), $out | Out-Null

dotnet test (Join-Path $root "tests\TubeWorks.Geometry.Tests") -c Release; if ($LASTEXITCODE) { throw "tests failed" }
dotnet build (Join-Path $root "src\TubeWorks.AddIn\TubeWorks.AddIn.csproj") -c Release "-p:SwInteropDir=$InteropDir" -o (Join-Path $work "bin"); if ($LASTEXITCODE) { throw "build failed" }
Copy-Item (Join-Path $work "bin\TubeWorks.AddIn.dll"), (Join-Path $work "bin\TubeWorks.Geometry.dll") $stage
Copy-Item -Recurse (Join-Path $work "bin\Icons") (Join-Path $stage "Icons")
Copy-Item (Join-Path $root "docs\*.md"), (Join-Path $root "docs\library-example.xml") (Join-Path $stage "Documentation")

$makensis = (Get-Command makensis.exe -ErrorAction SilentlyContinue).Source
if (-not $makensis) { $makensis = Join-Path ${env:ProgramFiles(x86)} "NSIS\makensis.exe" }
& $makensis -V2 "-DVERSION=$version" "-DSTAGE=$stage" "-DOUTDIR=$out" (Join-Path $root "installer\TubeWorks.nsi")
if ($LASTEXITCODE) { throw "makensis failed" }
Write-Host "Installer: $out\TubeWorksSetup-$version.exe"
