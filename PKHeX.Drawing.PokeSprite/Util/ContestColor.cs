using System;
using SkiaSharp;

namespace PKHeX.Drawing.PokeSprite;

/// <summary>
/// Utility class for getting the color of a contest category.
/// </summary>
public static class ContestColor
{
    public static SKColor Cool   => new SKColor((byte)(248), (byte)(152), (byte)(096));
    public static SKColor Beauty => new SKColor((byte)(128), (byte)(152), (byte)(248));
    public static SKColor Cute   => new SKColor((byte)(248), (byte)(168), (byte)(208));
    public static SKColor Clever => new SKColor((byte)(112), (byte)(224), (byte)(112));
    public static SKColor Tough  => new SKColor((byte)(248), (byte)(240), (byte)(056));

    /// <summary>
    /// Retrieves a predefined color based on the specified index.
    /// </summary>
    /// <param name="index">The index of the color to retrieve. Valid values are 0 through 4.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="index"/> is less than 0 or greater than 4.</exception>
    public static SKColor GetColor(int index) => index switch
    {
        0 => Cool,
        1 => Beauty,
        2 => Cute,
        3 => Clever,
        4 => Tough,
        _ => throw new ArgumentOutOfRangeException(nameof(index), index, null),
    };
}
