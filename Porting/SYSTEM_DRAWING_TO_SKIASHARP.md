# System.Drawing → SkiaSharp Migration Checklist (Phase 1)

Scope: the three `PKHeX.Drawing.*` projects. Goal: retarget them from `net10.0-windows`
(which drags in the Windows-only `System.Drawing.Common`) to plain `net10.0` on SkiaSharp,
so they run on macOS/Linux.

## Key finding: the surface is tiny and concentrated

Hand-written code in the three projects is only ~2,400 lines (the other ~65k in PokeSprite is
generated sprite resources, which don't change semantically). Of that, the **pixel-manipulation
engine is already platform-neutral** — it operates on `Span<byte>` BGRA buffers, which is exactly
SkiaSharp's default `SKColorType.Bgra8888` memory layout (byte order B,G,R,A). Those methods need
**no logic changes**, only a type swap on their `Color` parameters.

The only true `System.Drawing` couplings are: the bitmap type (`Bitmap`), the color value type
(`Color`), pixel locking (`LockBits`/`UnlockBits`), and ~13 lines of `Graphics` canvas drawing in
two files. There is **no image file IO** (`.Save`, `Image.FromStream`, `ImageFormat`) in these
projects — that lives in the WinForms layer, which is out of scope for Phase 1.

## Type mapping

| System.Drawing | SkiaSharp | Notes |
|---|---|---|
| `Bitmap` / `Image` | `SKBitmap` | Use `SKColorType.Bgra8888`, premul or unpremul per need |
| `Color` | `SKColor` | Ctor order differs: `new SKColor(r,g,b,a)` vs `Color.FromArgb(a,r,g,b)` |
| `Graphics` | `SKCanvas` | `new SKCanvas(skBitmap)` |
| `Graphics.FromImage(bmp)` | `new SKCanvas(bmp)` | |
| `g.DrawImage(img,x,y)` | `canvas.DrawBitmap(bmp,x,y)` | |
| `g.FillRectangle(brush,...)` | `canvas.DrawRect(x,y,w,h, paint)` | `paint.Color`, `paint.Style=Fill` |
| `g.DrawString(s,font,brush,x,y)` | `canvas.DrawText(s, x, y, sktypeface/skfont, paint)` | Baseline differs; see QR note |
| `Font` | `SKFont` / `SKPaint.TextSize` | |
| `SolidBrush` / `Pen` | `SKPaint` | |
| `SystemBrushes.ControlLightLight` | hardcode `new SKColor(0xF0,0xF0,0xF0)` | No system theme off-Windows |
| `PixelFormat.Format32bppArgb` | `SKColorType.Bgra8888` | |
| `bmp.LockBits(...)` + `Scan0` | `bmp.GetPixels(out IntPtr len)` | No lock/unlock needed — simpler |
| `bmp.UnlockBits(data)` | *(delete — not needed)* | SKBitmap pixels are directly addressable |
| `Color.FromArgb(a,c).ToArgb()` | pack manually: `(a<<24)|(r<<16)|(g<<8)|b` | Preserve exact byte semantics |
| `(Bitmap)img.Clone()` | `bmp.Copy()` | |
| `new Bitmap(w,h,fmt)` | `new SKBitmap(w,h, SKColorType.Bgra8888, SKAlphaType.Unpremul)` | |

## Call-site checklist (every System.Drawing touch point)

### PKHeX.Drawing/ImageUtil.cs — the pixel engine
- [ ] `extension(Bitmap bmp)` block (lines 14–134): change to `SKBitmap`. Replace the
      `GetBitmapData(out BitmapData, format)` lock/unlock pattern with
      `bmp.GetPixels(out var len)` + the existing `GetSpan(ptr, len)` helper (which carries over
      verbatim). **The lock/unlock try/finally blocks can be deleted** — SKBitmap pixels are
      always addressable, which simplifies every method here.
- [ ] `LayerImage(...)` (lines 145–151): `Graphics.FromImage` → `new SKCanvas(bmp)`,
      `gr.DrawImage` → `canvas.DrawBitmap`.
- [ ] `GetBitmap(...)` (lines 181–193): `new Bitmap(w,h,fmt)` → `new SKBitmap(...)`; write span
      via `GetPixels`.
- [ ] `Color` params in `BlendTransparentTo`, `ChangeAllColorTo`, `ChangeTransparentTo`,
      `WritePixels`, and statics `SetAllTransparencyTo`, `BlendAllTransparencyTo`, `ChangeAllTo`,
      `ChangeAllColorTo(Span,Color)`: change `Color`→`SKColor`; replace `c.R/.G/.B` (same names on
      SKColor: `.Red/.Green/.Blue`) and `Color.FromArgb(trans,c).ToArgb()` with the manual pack.
- [ ] **No change needed**: `SetAllUsedPixelsOpaque`, `RemovePixels`, `GetAverageColor`,
      `BlendColor`, `SetAllColorToGrayScale` (both), `GlowEdges`, `PollutePixels`,
      `CleanPollutedPixels`, `GetSpan` — pure `Span<byte>`/`int` math, already portable.

### PKHeX.Drawing/ColorUtil.cs
- [ ] Swap `Color`→`SKColor` (3 returns, 1 param-pair). Only API used is `Color.FromArgb(r,g,b)`
      → `new SKColor(r,g,b)` and `.R/.G/.B` → `.Red/.Green/.Blue`. Pure math otherwise.

### PKHeX.Drawing.Misc/QR/QRImageUtil.cs — the only real canvas work
- [ ] `Graphics.FromImage` ×2 → `new SKCanvas`.
- [ ] `FillRectangle(SystemBrushes.ControlLightLight, ...)` ×2 → `canvas.DrawRect` with a
      hardcoded light-gray `SKPaint`.
- [ ] `DrawImage` ×2 → `DrawBitmap`.
- [ ] `DrawString` ×4 (caption lines under the QR) → `canvas.DrawText`. **Watch the baseline**:
      `DrawString`'s y is the top of the text; `DrawText`'s y is the baseline. Add roughly the
      font ascent (~`font.Size`) to each y, then eyeball against a Windows render.
- [ ] `Font`/`Brush` locals → `SKFont`/`SKPaint`.

### PKHeX.Drawing.PokeSprite — sprite builders & utils
- [ ] No direct `Graphics` use. Surface is `Bitmap` return/param types and `Color.FromArgb` in the
      color tables (`TypeColor`, `StatusColor`, `ContestColor`, tera color). Mechanical type swaps.
- [ ] `SpriteBuilder : ISpriteBuilder<Bitmap>` (and abstract `Bitmap` props) → `ISpriteBuilder<SKBitmap>`.
      This generic interface lives in Core and is **already parameterized on the image type** —
      it's the clean seam; no Core change required, just instantiate with `SKBitmap`.
- [ ] `Resources.Designer.cs` (generated): each sprite property currently returns `Bitmap`. After
      retargeting, regenerate or post-process so resources surface as `SKBitmap` (decode the
      embedded PNG bytes via `SKBitmap.Decode`). Wide but mechanical; consider a small helper
      `SKBitmap FromResource(byte[] png) => SKBitmap.Decode(png)`.

## Project file changes (all three)
- [ ] `<TargetFramework>net10.0-windows</TargetFramework>` → `net10.0`
- [ ] Remove `<PackageReference Include="System.Drawing.Common" .../>` (PKHeX.Drawing)
- [ ] Add `<PackageReference Include="SkiaSharp" Version="3.*" />`
- [ ] Keep `QRCoder` and `System.Resources.Extensions` (both cross-platform)

## Validation
- [ ] Build all three on macOS/Linux.
- [ ] Pixel-diff a handful of rendered sprites (shiny tint, glow edges, grayscale, layered slot
      sprite) against a Windows build. The BGRA layout matches, so output should be identical;
      any difference points to a premul/alpha-type mismatch on `SKBitmap` construction.
- [ ] Visually check one QR image (the only place text is drawn) for baseline offset.

## Note on strategy: direct type-swap vs. abstraction
Because (a) `ISpriteBuilder<T>` already generalizes the image type and (b) Avalonia renders via
Skia natively (an `SKBitmap` flows straight to the UI with no conversion), the recommended path is
a **direct swap to SkiaSharp types**, not a new `IImage` abstraction. An abstraction only earns its
keep if you intend to keep a parallel Windows/System.Drawing build alive — if you don't, it's pure
indirection. The scaffold in `Porting/scaffold/` takes the direct-swap approach.
