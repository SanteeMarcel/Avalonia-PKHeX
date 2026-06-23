# Phase 1 scaffold — SkiaSharp drawing layer

These files are **proposals to apply on your Mac**, not compiled here. They take the
direct-type-swap approach (`Bitmap`→`SKBitmap`, `Color`→`SKColor`) described in
`../SYSTEM_DRAWING_TO_SKIASHARP.md`.

## What's here
- `ImageUtil.cs` — full SkiaSharp port of `PKHeX.Drawing/ImageUtil.cs`. The pixel-math methods
  are verbatim; the bitmap/canvas/color members are swapped. **The lock/unlock dance is gone.**
- `ColorUtil.cs` — full SkiaSharp port of `PKHeX.Drawing/ColorUtil.cs`.
- `PKHeX.Drawing.csproj`, `PKHeX.Drawing.PokeSprite.csproj`, `PKHeX.Drawing.Misc.csproj` —
  proposed project files (net10.0 + SkiaSharp).

## How to apply (suggested order)
1. **Work on a branch.** `git checkout -b port/skia-drawing`.
2. Replace the three `.csproj` files with these versions.
3. Replace `PKHeX.Drawing/ImageUtil.cs` and `PKHeX.Drawing/ColorUtil.cs` with these.
4. `dotnet build PKHeX.Drawing/PKHeX.Drawing.csproj` — fix any caller fallout in this project first.
5. Port `PKHeX.Drawing.Misc/QR/QRImageUtil.cs` by hand (the only `Graphics`/`DrawString` work —
   ~13 lines; see the QR section of the checklist). Then build `PKHeX.Drawing.Misc`.
6. In `PKHeX.Drawing.PokeSprite`, change `ISpriteBuilder<Bitmap>` → `ISpriteBuilder<SKBitmap>`
   and resolve the generated `Resources.Designer.cs` (sprites as `SKBitmap`). Build it.
7. Build all three together on macOS, then pixel-diff a few sprites vs. a Windows reference.

## Things to verify (because this wasn't compiled here)
- **C# 14 `extension(...)` syntax**: the repo uses it (LangVersion 14); the port keeps it on
  `SKBitmap`. Confirm it resolves as expected.
- **Alpha type**: bitmaps are created `Unpremul` so the raw BGRA bytes match the original
  semantics. If composited sprites look off at edges, check premultiplied-alpha assumptions in
  `SKCanvas.DrawBitmap`.
- **`SKBitmap.GetPixels(out IntPtr len)`** returns the byte length; the cast to `int` is fine for
  sprite-sized images.
- **Linux/CI**: add `SkiaSharp.NativeAssets.Linux` (and `libfontconfig1`/`libfreetype6` on the
  runner) or Skia won't load natively. macOS needs no extra native asset.

## Not in scope for Phase 1
Image *file IO* (`.Save`, PNG encode/decode for clipboard/export) lives in the WinForms layer and
is handled when that layer is replaced (Phase 2+). SkiaSharp covers it via `SKImage.Encode` /
`SKBitmap.Decode` when you get there.
