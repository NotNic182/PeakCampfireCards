#!/bin/bash
# Full build: regenerate from the sheets, preflight, compile, package. Usage: tools/build.sh [--release] [--install]
set -e
cd "$(dirname "$0")/.."
export PATH="$PATH:$HOME/.dotnet/tools"
RELEASE=""; INSTALL=""
for a in "$@"; do
  case "$a" in
    --release) RELEASE="--release" ;;
    --install) INSTALL="--install" ;;
  esac
done
python -I tools/gen.py
python -I tools/preflight.py $RELEASE
PEAKARG=""; [ -n "$PEAK_DIR" ] && PEAKARG="-p:PeakDir=$PEAK_DIR"
dotnet build src/CampfireCards/CampfireCards.csproj -c Release -nologo -v m $PEAKARG
python -I tools/package.py $INSTALL
