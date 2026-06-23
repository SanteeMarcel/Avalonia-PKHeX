#!/usr/bin/env bash
# Phase 1 — PokeSprite type swaps (System.Drawing -> SkiaSharp).
# Run from the repo root, AFTER copying in Resources.cs + the new .csproj.
# macOS BSD sed syntax (sed -i '').  Review `git diff` afterwards.
set -euo pipefail

P=PKHeX.Drawing.PokeSprite
FILES=(
  "$P/Builder/SpriteBuilder.cs"
  "$P/Builder/SpriteBuilder5668.cs"
  "$P/Builder/SpriteBuilder5668a.cs"
  "$P/Builder/SpriteBuilder5668c.cs"
  "$P/Util/SpriteUtil.cs"
  "$P/Util/TypeColor.cs"
  "$P/Util/StatusColor.cs"
  "$P/Util/ContestColor.cs"
)

for f in "${FILES[@]}"; do
  sed -i '' -E \
    -e 's/using System\.Drawing;/using SkiaSharp;/' \
    `# Color.FromArgb 4-arg (A,R,G,B) -> SKColor(R,G,B,A)` \
    -e 's/Color\.FromArgb\(([^,)]+), *([^,)]+), *([^,)]+), *([^,)]+)\)/new SKColor((byte)(\2), (byte)(\3), (byte)(\4), (byte)(\1))/g' \
    `# Color.FromArgb(index) single packed-int overload (0xAARRGGBB)` \
    -e 's/Color\.FromArgb\(index\)/new SKColor((byte)(index >> 16), (byte)(index >> 8), (byte)index, (byte)(index >> 24))/g' \
    `# Color.FromArgb 3-arg (R,G,B)` \
    -e 's/Color\.FromArgb\(([^,)]+), *([^,)]+), *([^,)]+)\)/new SKColor((byte)(\1), (byte)(\2), (byte)(\3))/g' \
    `# named System.Drawing colors used in these files` \
    -e 's/Color\.Lime/new SKColor(0x00, 0xFF, 0x00)/g' \
    -e 's/Color\.DodgerBlue/new SKColor(0x1E, 0x90, 0xFF)/g' \
    -e 's/Color\.DarkOrange/new SKColor(0xFF, 0x8C, 0x00)/g' \
    -e 's/Color\.Yellow/new SKColor(0xFF, 0xFF, 0x00)/g' \
    -e 's/Color\.LightYellow/new SKColor(0xFF, 0xFF, 0xE0)/g' \
    `# bare Color type -> SKColor (word-bounded; leaves TypeColor/StatusColor/etc untouched)` \
    -e 's/\bColor\b/SKColor/g' \
    `# Bitmap/Image types -> SKBitmap (word-bounded; leaves GetBitmap/GetBitmapData/ImageUtil/baseImage)` \
    -e 's/\bBitmap\b/SKBitmap/g' \
    -e 's/\bImage\b/SKBitmap/g' \
    `# ImageUtil.GetBitmap lost its PixelFormat parameter` \
    -e 's/, *baseSprite\.PixelFormat\)/)/g' \
    -e 's/, *b\.PixelFormat\)/)/g' \
    `# byte[] pixel accessor was renamed` \
    -e 's/\.GetBitmapData\(\)/.GetPixelBytes()/g' \
    "$f"
  echo "swapped: $f"
done

echo "Done. Review with: git diff -- $P"
