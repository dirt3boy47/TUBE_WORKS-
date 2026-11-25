#!/usr/bin/env bash
# Build the Release add-in and the Windows setup program on Linux (or in CI). Needs: .NET SDK 8, NSIS (makensis), curl, unzip.
# The add-in is compiled against the SOLIDWORKS 2016 interop assemblies (oldest API level that has every call used),
# taken from the public NuGet package "SolidWorks.Interop" 16.10.0. They are not shipped: SOLIDWORKS provides them at run time.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="$(sed -n 's:.*<TubeWorksVersion>\(.*\)</TubeWorksVersion>.*:\1:p' "$ROOT/Directory.Build.props")"
WORK="$ROOT/installer/work"
rm -rf "$WORK"; mkdir -p "$WORK/interop" "$WORK/stage/Documentation" "$ROOT/installer/out"

curl -sSfL -o "$WORK/interop.nupkg" https://api.nuget.org/v3-flatcontainer/solidworks.interop/16.10.0/solidworks.interop.16.10.0.nupkg
unzip -o -j -q "$WORK/interop.nupkg" 'lib/SolidWorks.Interop.sldworks.dll' 'lib/SolidWorks.Interop.swconst.dll' 'lib/SolidWorks.Interop.swpublished.dll' -d "$WORK/interop"

dotnet test "$ROOT/tests/TubeWorks.Geometry.Tests" -c Release
dotnet build "$ROOT/src/TubeWorks.AddIn/TubeWorks.AddIn.csproj" -c Release -p:SwInteropDir="$WORK/interop" -o "$WORK/bin"

cp "$WORK/bin/TubeWorks.AddIn.dll" "$WORK/bin/TubeWorks.Geometry.dll" "$WORK/stage/"
cp -r "$WORK/bin/Icons" "$WORK/stage/Icons"
cp "$ROOT/docs/"*.md "$ROOT/docs/library-example.xml" "$WORK/stage/Documentation/"
# Interop assemblies must not be in the package.
if ls "$WORK/stage" | grep -qi interop; then echo "interop assembly in stage folder" >&2; exit 1; fi

makensis -V2 -DVERSION="$VERSION" -DSTAGE="$WORK/stage" -DOUTDIR="$ROOT/installer/out" "$ROOT/installer/TubeWorks.nsi"
echo "Installer: $ROOT/installer/out/TubeWorksSetup-$VERSION.exe"
