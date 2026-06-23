# PokeSprite — remaining type-swap recipe

After dropping in `Resources.cs` (facade) and the updated `.csproj`, the 7 hand-written source
files still reference `System.Drawing`. These are mechanical find/replaces plus 3 real gotchas.

## Files to edit
`Builder/SpriteBuilder.cs`, `Builder/SpriteBuilder5668.cs`, `Builder/SpriteBuilder5668a.cs`,
`Builder/SpriteBuilder5668c.cs`, `Util/SpriteUtil.cs`, `Util/TypeColor.cs`, `Util/StatusColor.cs`,
`Util/ContestColor.cs`.

## Global find/replace (all 8 files)
- `using System.Drawing;`  →  `using SkiaSharp;`
- `Bitmap`                 →  `SKBitmap`   (whole word)
- `Color` (the type)       →  `SKColor`    (whole word — careful not to hit identifiers like `TypeColor`)
- `ISpriteBuilder<Bitmap>` →  `ISpriteBuilder<SKBitmap>`   (SpriteBuilder.cs:7)

## Color constructions
- `Color.FromArgb(r, g, b)`            →  `new SKColor((byte)r, (byte)g, (byte)b)`
- `Color.FromArgb(a, r, g, b)`         →  `new SKColor((byte)r, (byte)g, (byte)b, (byte)a)`
  (note the **argument-order flip**: System.Drawing is A,R,G,B; SKColor is R,G,B,A)
- Named colors used in `SpriteUtil.cs`:
  - `Color.Lime`       →  `new SKColor(0x00, 0xFF, 0x00)`
  - `Color.DodgerBlue` →  `new SKColor(0x1E, 0x90, 0xFF)`
  - `Color.DarkOrange` →  `new SKColor(0xFF, 0x8C, 0x00)`
  - `Color.Yellow`     →  `new SKColor(0xFF, 0xFF, 0x00)`
  - `Color.LightYellow` (TypeColor.cs) → `new SKColor(0xFF, 0xFF, 0xE0)`

## The 3 real gotchas

1. **`ImageUtil.GetBitmap` signature changed** — the SkiaSharp port dropped the `PixelFormat`
   parameter (SKBitmap is always BGRA8888 here). Update these call sites:
   - `SpriteUtil.cs:82`:  `ImageUtil.GetBitmap(pixels, baseSprite.Width, baseSprite.Height, baseSprite.PixelFormat)`
     →  `ImageUtil.GetBitmap(pixels, baseSprite.Width, baseSprite.Height)`
   - `SpriteBuilder.cs:153`: `ImageUtil.GetBitmap(pixels, b.Width, b.Height, b.PixelFormat)`
     →  `ImageUtil.GetBitmap(pixels, b.Width, b.Height)`
   (SKBitmap has no `.PixelFormat`; it has `.ColorType` if you ever need it.)

2. **`Color.FromArgb(int)` single-int overload** — `SpriteUtil.cs:167` `Color.FromArgb(index)`
   unpacks a packed 0xAARRGGBB int. SKColor's `SKColor(uint)` is not equivalent in byte order, so
   unpack explicitly:
   ```csharp
   var color = new SKColor((byte)(index >> 16), (byte)(index >> 8), (byte)index, (byte)(index >> 24));
   ```

3. **`GetBitmapData()` → `GetPixelBytes()`** — `SpriteUtil.cs:237` `baseSprite.GetBitmapData()`
   (the byte[] overload) was renamed in the ported ImageUtil. Change to
   `baseSprite.GetPixelBytes()`. Likewise any `.GetBitmapData(...)`/`.SetBitmapData(...)` extension
   calls map to the new names: `GetPixelSpanRW()`, `CopyPixelsTo(...)`, `SetPixels(...)`.

## Casts
`(Bitmap?)Resources.ResourceManager.GetObject(...)` → the facade already returns `SKBitmap?`, so the
cast becomes `(SKBitmap?)` or can be deleted. Affected: `SpriteUtil.cs:58,61,62`,
`SpriteBuilder.cs:160,164,179,202`.

## Then build
`dotnet build PKHeX.Drawing.PokeSprite/PKHeX.Drawing.PokeSprite.csproj`
Expect a short tail of CS0117/CS1503 errors if a named sprite or a cast was missed — paste them and
they're quick fixes.
