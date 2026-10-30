#!/usr/bin/env bash
# Downloads the Code New Roman fonts System.Drawing.Common's tests use. They come from dotnet/runtime-assets (the
# commit tools/lift/lift_drawing_tests.py lifted) and are not committed: the font's licence text both grants the
# SIL Open Font License 1.1 and forbids redistribution.
set -euo pipefail
cd "$(dirname "$0")/../.."
COMMIT=848e27e0f5c1c2cb9bf8584d13370e273c18ee57
DEST=test/webForCore.System.Drawing.Common.Tests/TestData/fonts
mkdir -p "$DEST"
for f in CodeNewRoman.otf CodeNewRoman.ttf; do
  curl -fsSL -o "$DEST/$f" "https://raw.githubusercontent.com/dotnet/runtime-assets/$COMMIT/src/System.Drawing.Common.TestData/fonts/$f"
done
echo "Fonts in $DEST"
