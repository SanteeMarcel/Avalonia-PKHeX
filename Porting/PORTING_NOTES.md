# Porting notes — gotchas discovered during the macOS/Linux port

Hard-won specifics that aren't obvious and will bite again if forgotten. Companion to
`SYSTEM_DRAWING_TO_SKIASHARP.md`.

## 1. Decoded sprites must be forced to Bgra8888
`SKBitmap.Decode(...)` returns **Rgba8888** by default on macOS. `ImageUtil`'s pixel math
assumes BGRA byte order (the old `System.Drawing` `Format32bppArgb` layout), so without a
conversion, *tinted* sprites (shiny glow, tera/encounter color) come out with red and blue
swapped — while plain sprites look fine, which makes it easy to miss.

Fix: the resource facades (`Properties/Resources.cs` in PokeSprite and Misc) convert every
decoded sprite via `bmp.Copy(SKColorType.Bgra8888)` when the decode colortype isn't already
BGRA. Any new code path that calls `SKBitmap.Decode` must do the same.

## 2. SkiaSharp version must be ONE value across the whole solution
The drawing layer was first built against **SkiaSharp 3.116.1**. Avalonia 12.0.0 resolves
**SkiaSharp 3.119.3-preview.1.1** (managed + native assets). Running the Avalonia app with the
drawing DLLs compiled against 3.116 but a 3.119 native lib loaded = **silent native segfault**
(mixed-version SkiaSharp in one process).

Fix: pin every project that touches SkiaSharp to the exact version Avalonia uses. Currently
`3.119.3-preview.1.1` in `PKHeX.Drawing.csproj` and `PKHeX.Drawing.Misc.csproj`. If Avalonia is
upgraded, re-check with:
`dotnet list PKHeX.Avalonia/PKHeX.Avalonia.csproj package --include-transitive | grep -i skia`
and realign the drawing projects to match.

## 3. Avalonia 12's native macOS file picker crashes
`StorageProvider.OpenFilePickerAsync` segfaults on macOS inside `libAvaloniaNative.dylib`
(`NSOpenPanel` completion callback) — an upstream Avalonia bug, not ours. The crash report's
faulting frame is `StorageProvider::OpenFileDialog`.

Workaround in the shell: a path TextBox + Load button (no native dialog). Revisit when on an
Avalonia version where the picker is fixed, or use a managed dialog. Drag-and-drop is also an
option but Avalonia 12 changed that API (`DataFormats` → `DataFormat`, `e.Data` reshaped).

## 4. Resource model change vs upstream
Sprites are no longer serialized `System.Drawing.Bitmap` objects in `.resx`; they're the raw
PNG files embedded by logical name (basenames are globally unique in both PokeSprite and Misc)
and decoded to `SKBitmap` on demand by the `Resources` facade. The old `Resources.Designer.cs`
and `Resources.resx` are excluded via the csproj (`Compile Remove` / `EmbeddedResource Remove`)
rather than deleted.

## 5. Solution build on macOS
`PKHeX.slnx` still contains `PKHeX.WinForms`, which cannot build off Windows. Build the
cross-platform projects individually (or filter the WinForms project out of CI on non-Windows).
The app to run is `PKHeX.Avalonia`; `PKHeX.SpriteTest` is the headless sprite-verification harness.
