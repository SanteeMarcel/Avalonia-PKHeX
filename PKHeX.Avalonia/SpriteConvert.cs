using System;
using System.IO;
using Avalonia.Media.Imaging;
using PKHeX.Core;
using PKHeX.Drawing.PokeSprite;
using SkiaSharp;

namespace PKHeX.Avalonia;

/// <summary>Shared helpers for turning Core/Skia sprites into Avalonia bitmaps.</summary>
public static class SpriteConvert
{
    public static Bitmap? FromPkm(PKM pk)
    {
        try { return ToAvalonia(pk.Sprite()); }
        catch (Exception ex) { Console.Error.WriteLine($"sprite {pk.Species}: {ex.Message}"); return null; }
    }

    public static Bitmap? ToAvalonia(SKBitmap? skb)
    {
        if (skb is null)
            return null;
        using var image = SKImage.FromBitmap(skb);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream();
        data.SaveTo(ms);
        ms.Position = 0;
        return new Bitmap(ms);
    }
}

public sealed class SlotVm(Bitmap? image, string label, int index = -1, PKM? pk = null, Bitmap? badge = null, bool isParty = false)
{
    public Bitmap? Image { get; } = image;
    public string Label { get; } = label;
    public int Index { get; } = index;
    public PKM? Pk { get; } = pk;
    /// <summary>Legality indicator overlay (valid check / warning), or null.</summary>
    public Bitmap? Badge { get; } = badge;
    /// <summary>True when this slot is a party member (vs a box slot).</summary>
    public bool IsParty { get; } = isParty;
}
