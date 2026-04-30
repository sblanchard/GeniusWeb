#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

OUTROOT="${1:-publish}"

for RID in linux-x64 linux-arm64 win-x64 osx-arm64; do
    echo "=== publishing $RID ==="
    dotnet publish src/GeniusOnline.Web/GeniusOnline.Web.csproj \
        -c Release \
        -r "$RID" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:DebugType=none \
        -o "$OUTROOT/$RID"
done

echo "done — artifacts in $OUTROOT/<rid>/"
