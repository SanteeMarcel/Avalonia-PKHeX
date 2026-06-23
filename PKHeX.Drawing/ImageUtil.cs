// Phase 1 scaffold — SkiaSharp port of PKHeX.Drawing/ImageUtil.cs
//
// Strategy (see ../SYSTEM_DRAWING_TO_SKIASHARP.md):
//  * Bitmap                 -> SKBitmap (SKColorType.Bgra8888, byte order B,G,R,A)
//  * Color                  -> SKColor
//  * LockBits/UnlockBits    -> SKBitmap.GetPixels(out len); pixels are always addressable,
//                              so the lock/unlock try-finally dance is DELETED (simpler).
//  * Graphics.FromImage     -> new SKCanvas(skBitmap)
//
// The pure Span<byte>/Span<int> math methods are copied VERBATIM from the original — the
// existing BGRA assumptions already match SkiaSharp's Bgra8888 memory layout. Only the
// Bitmap/Color/Graphics-touching members changed.
//
// NOTE: not compiled in this environment. Build + pixel-diff on macOS to confirm parity.
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace PKHeX.Drawing;

/// <summary>
/// Image Layering/Blending Utility (SkiaSharp).
/// </summary>
public static class ImageUtil
{
    // SkiaSharp bitmaps used here are always BGRA8888 / unpremultiplied so the raw byte
    // layout matches the [B,G,R,A] assumptions in the span routines below.
    private static SKBitmap NewBgra(int width, int height)
        => new(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);

    extension(SKBitmap bmp)
    {
        /// <summary>
        /// Returns a writable span over the bitmap's raw BGRA pixel memory.
        /// Unlike the old System.Drawing path, no lock/unlock is required.
        /// </summary>
        public Span<byte> GetPixelSpanRW()
        {
            var ptr = bmp.GetPixels(out var len);
            return GetSpan(ptr, (int)len);
        }

        public void CopyPixelsTo(Span<byte> data) => bmp.GetPixelSpanRW().CopyTo(data);

        public void CopyPixelsTo(Span<int> data)
        {
            var src = MemoryMarshal.Cast<byte, int>(bmp.GetPixelSpanRW());
            src.CopyTo(data);
        }

        public void SetPixels(ReadOnlySpan<byte> data) => data.CopyTo(bmp.GetPixelSpanRW());

        public void SetPixels(Span<int> data)
        {
            var dest = MemoryMarshal.Cast<byte, int>(bmp.GetPixelSpanRW());
            data.CopyTo(dest);
        }

        public byte[] GetPixelBytes() => [.. bmp.GetPixelSpanRW()];

        public void ToGrayscale(float intensity)
        {
            if (intensity is <= 0.01f or > 1f)
                return;
            SetAllColorToGrayScale(bmp.GetPixelSpanRW(), intensity);
        }

        public void ChangeOpacity(double trans)
        {
            if (trans is <= 0.01f or > 1f)
                return;
            SetAllTransparencyTo(bmp.GetPixelSpanRW(), trans);
        }

        public void BlendTransparentTo(SKColor c, byte trans, int start = 0, int end = -1)
        {
            var data = bmp.GetPixelSpanRW();
            if (end == -1)
                end = data.Length;
            BlendAllTransparencyTo(data[start..end], c, trans);
        }

        public void ChangeAllColorTo(SKColor c) => ChangeAllColorTo(bmp.GetPixelSpanRW(), c);

        public void ChangeTransparentTo(SKColor c, byte trans, int start = 0, int end = -1)
        {
            var data = bmp.GetPixelSpanRW();
            if (end == -1)
                end = data.Length;
            SetAllTransparencyTo(data[start..end], c, trans);
        }

        public void WritePixels(SKColor c, int start, int end)
            => ChangeAllTo(bmp.GetPixelSpanRW(), c, start, end);

        public int GetAverageColor() => GetAverageColor(bmp.GetPixelSpanRW());
    }

    private static Span<byte> GetSpan(nint ptr, int length)
        => MemoryMarshal.CreateSpan(ref Unsafe.AddByteOffset(ref Unsafe.NullRef<byte>(), ptr), length);

    // ---- ARGB packing helpers (preserve exact byte semantics of the old Color.ToArgb()) ----
    // Original wrote Color.FromArgb(...).ToArgb() as a little-endian int into a BGRA buffer,
    // landing bytes as [B,G,R,A]. These reproduce that packed int exactly.
    private static int ToArgb(SKColor c) => (c.Alpha << 24) | (c.Red << 16) | (c.Green << 8) | c.Blue;
    private static int ToArgb(byte alpha, SKColor c) => (alpha << 24) | (c.Red << 16) | (c.Green << 8) | c.Blue;

    public static SKBitmap LayerImage(SKBitmap baseLayer, SKBitmap overLayer, int x, int y, double transparency)
    {
        overLayer = CopyChangeOpacity(overLayer, transparency);
        return LayerImage(baseLayer, overLayer, x, y);
    }

    public static SKBitmap LayerImage(SKBitmap baseLayer, SKBitmap overLayer, int x, int y)
    {
        var bmp = baseLayer.Copy();
        using var canvas = new SKCanvas(bmp);
        canvas.DrawBitmap(overLayer, new SKRect(x, y, x + overLayer.Width, y + overLayer.Height));
        return bmp;
    }

    public static SKBitmap CopyChangeOpacity(SKBitmap img, double trans)
    {
        var bmp = img.Copy();
        bmp.ChangeOpacity(trans);
        return bmp;
    }

    public static SKBitmap CopyChangeAllColorTo(SKBitmap img, SKColor c)
    {
        var bmp = img.Copy();
        bmp.ChangeAllColorTo(c);
        return bmp;
    }

    public static SKBitmap CopyChangeTransparentTo(SKBitmap img, SKColor c, byte trans, int start = 0, int end = -1)
    {
        var bmp = img.Copy();
        bmp.ChangeTransparentTo(c, trans, start, end);
        return bmp;
    }

    public static SKBitmap CopyWritePixels(SKBitmap img, SKColor c, int start, int end)
    {
        var bmp = img.Copy();
        bmp.WritePixels(c, start, end);
        return bmp;
    }

    public static SKBitmap GetBitmap(ReadOnlySpan<byte> data, int width, int height, int length)
    {
        var bmp = NewBgra(width, height);
        data[..length].CopyTo(bmp.GetPixelSpanRW());
        return bmp;
    }

    public static SKBitmap GetBitmap(ReadOnlySpan<byte> data, int width, int height)
        => GetBitmap(data, width, height, data.Length);

    // ===================================================================================
    // Below: pure span math — copied verbatim from the original except Color -> SKColor
    // and Color.FromArgb(...).ToArgb() -> ToArgb(...). No platform dependency.
    // ===================================================================================

    public static void SetAllUsedPixelsOpaque(Span<byte> data, byte threshold = TransparencyThresholdHalf)
    {
        for (int i = data.Length - 4; i >= 0; i -= 4)
        {
            if (data[i + 3] >= threshold)
                data[i + 3] = 0xFF;
        }
    }

    public static void RemovePixels(Span<byte> pixels, ReadOnlySpan<byte> original, byte threshold = TransparencyThresholdHalf)
    {
        var arr = MemoryMarshal.Cast<byte, int>(pixels);
        for (int i = original.Length - 4; i >= 0; i -= 4)
        {
            if (original[i + 3] >= threshold)
                arr[i >> 2] = 0;
        }
    }

    private static void SetAllTransparencyTo(Span<byte> data, double trans)
    {
        for (int i = data.Length - 4; i >= 0; i -= 4)
            data[i + 3] = (byte)(data[i + 3] * trans);
    }

    private static void SetAllTransparencyTo(Span<byte> data, SKColor c, byte trans, byte threshold = TransparencyThresholdHalf)
    {
        var arr = MemoryMarshal.Cast<byte, int>(data);
        var value = ToArgb(trans, c);
        for (int i = data.Length - 4; i >= 0; i -= 4)
        {
            if (data[i + 3] < threshold)
                arr[i >> 2] = value;
        }
    }

    private static void BlendAllTransparencyTo(Span<byte> data, SKColor c, byte trans, byte threshold = TransparencyThresholdHalf)
    {
        var arr = MemoryMarshal.Cast<byte, int>(data);
        var value = ToArgb(trans, c);
        for (int i = data.Length - 4; i >= 0; i -= 4)
        {
            var alpha = data[i + 3];
            if (alpha < threshold)
                arr[i >> 2] = value;
            else if (alpha != 0xFF)
                arr[i >> 2] = BlendColor(arr[i >> 2], value);
        }
    }

    private static int GetAverageColor(Span<byte> data, byte threshold = TransparencyThreshold16)
    {
        long r = 0, g = 0, b = 0;
        int count = 0;
        for (int i = data.Length - 4; i >= 0; i -= 4)
        {
            var alpha = data[i + 3];
            if (alpha < threshold)
                continue;
            r += data[i + 2];
            g += data[i + 1];
            b += data[i + 0];
            count++;
        }
        if (count == 0)
            return 0;
        byte R = (byte)(r / count);
        byte G = (byte)(g / count);
        byte B = (byte)(b / count);
        return (0xFF << 24) | (R << 16) | (G << 8) | B;
    }

    private static int BlendColor(int color1, int color2, double amount = 0.2)
    {
        var a1 = (color1 >> 24) & 0xFF;
        var r1 = (color1 >> 16) & 0xFF;
        var g1 = (color1 >> 8) & 0xFF;
        var b1 = color1 & 0xFF;

        var a2 = (color2 >> 24) & 0xFF;
        var r2 = (color2 >> 16) & 0xFF;
        var g2 = (color2 >> 8) & 0xFF;
        var b2 = color2 & 0xFF;

        byte a = (byte)((a1 * amount) + (a2 * (1 - amount)));
        byte r = (byte)((r1 * amount) + (r2 * (1 - amount)));
        byte g = (byte)((g1 * amount) + (g2 * (1 - amount)));
        byte b = (byte)((b1 * amount) + (b2 * (1 - amount)));

        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    private static void ChangeAllTo(Span<byte> data, SKColor c, int start, int end)
    {
        var arr = MemoryMarshal.Cast<byte, int>(data[start..end]);
        var value = ToArgb(c);
        arr.Fill(value);
    }

    public static void ChangeAllColorTo(Span<byte> data, SKColor c)
    {
        byte R = c.Red;
        byte G = c.Green;
        byte B = c.Blue;
        for (int i = data.Length - 4; i >= 0; i -= 4)
        {
            if (data[i + 3] == 0)
                continue;
            data[i + 0] = B;
            data[i + 1] = G;
            data[i + 2] = R;
        }
    }

    private static void SetAllColorToGrayScale(Span<byte> data, float intensity)
    {
        if (intensity <= 0f)
            return;

        if (intensity >= 0.999f)
        {
            SetAllColorToGrayScale(data);
            return;
        }

        float inverse = 1f - intensity;
        for (int i = data.Length - 4; i >= 0; i -= 4)
        {
            if (data[i + 3] == 0)
                continue;
            byte greyS = (byte)((0.3 * data[i + 2]) + (0.59 * data[i + 1]) + (0.11 * data[i + 0]));
            data[i + 0] = (byte)((data[i + 0] * inverse) + (greyS * intensity));
            data[i + 1] = (byte)((data[i + 1] * inverse) + (greyS * intensity));
            data[i + 2] = (byte)((data[i + 2] * inverse) + (greyS * intensity));
        }
    }

    private static void SetAllColorToGrayScale(Span<byte> data)
    {
        for (int i = data.Length - 4; i >= 0; i -= 4)
        {
            if (data[i + 3] == 0)
                continue;
            byte greyS = (byte)((0.3 * data[i + 2]) + (0.59 * data[i + 1]) + (0.11 * data[i + 0]));
            data[i + 0] = greyS;
            data[i + 1] = greyS;
            data[i + 2] = greyS;
        }
    }

    public static void GlowEdges(Span<byte> data, byte blue, byte green, byte red, int width, int reach = 3, double amount = 0.0777)
    {
        for (int i = data.Length - 4; i >= 0; i -= 4)
            data[i + PollutePixelColorIndex] = 0;
        PollutePixels(data, width, reach, amount);
        CleanPollutedPixels(data, blue, green, red);
    }

    private const int PollutePixelColorIndex = 0; // Blue
    private const byte TransparencyThresholdHalf = 0x80;
    private const byte TransparencyThreshold16 = 0x10;

    private static void PollutePixels(Span<byte> data, int width, int reach, double amount, byte threshold = TransparencyThresholdHalf)
    {
        int stride = width * 4;
        int height = data.Length / stride;
        for (int i = data.Length - 4; i >= 0; i -= 4)
        {
            if (data[i + 3] < threshold)
                continue;

            int x = (i % stride) / 4;
            int y = (i / stride);
            {
                int left = Math.Max(0, x - reach);
                int right = Math.Min(width - 1, x + reach);
                int top = Math.Max(0, y - reach);
                int bottom = Math.Min(height - 1, y + reach);
                for (int ix = left; ix <= right; ix++)
                {
                    for (int iy = top; iy <= bottom; iy++)
                    {
                        var c = 4 * (ix + (iy * width));
                        ref var b = ref data[c + PollutePixelColorIndex];
                        b += (byte)(amount * (0xFF - b));
                    }
                }
            }
        }
    }

    private static void CleanPollutedPixels(Span<byte> data, byte blue, byte green, byte red)
    {
        for (int i = data.Length - 4; i >= 0; i -= 4)
        {
            var transparency = data[i + PollutePixelColorIndex];
            if (transparency == 0)
                continue;

            data[i + 0] = blue;
            data[i + 1] = green;
            data[i + 2] = red;
            data[i + 3] = transparency;
        }
    }
}
