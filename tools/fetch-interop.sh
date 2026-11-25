#!/usr/bin/env bash
# CI / Linux only: download SOLIDWORKS interop assemblies from the public Xarial.XCad.SolidWorks NuGet package,
# so that the add-in can be COMPILED without a SOLIDWORKS installation. This checks that every API member
# used exists with the used signature. It does not run anything in SOLIDWORKS.
# On Windows, build against your own installation instead (api\redist), which is the default.
set -euo pipefail
VERSION="${1:-0.8.3}"
DEST="$(cd "$(dirname "$0")/.." && pwd)/interop"
mkdir -p "$DEST"
TMP="$(mktemp -d)"
curl -sSfL -o "$TMP/x.nupkg" "https://api.nuget.org/v3-flatcontainer/xarial.xcad.solidworks/$VERSION/xarial.xcad.solidworks.$VERSION.nupkg"
unzip -o -j -q "$TMP/x.nupkg" 'lib/net461/SolidWorks.Interop.*' -d "$DEST"
rm -rf "$TMP"
echo "Interop assemblies in $DEST"
