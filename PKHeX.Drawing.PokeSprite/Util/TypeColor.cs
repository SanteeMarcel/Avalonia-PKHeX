using System;
using SkiaSharp;
using PKHeX.Core;
using static PKHeX.Core.MoveType;

namespace PKHeX.Drawing.PokeSprite;

/// <summary>
/// Utility class for getting the color of a <see cref="MoveType"/>.
/// </summary>
public static class TypeColor
{
    /// <summary>
    /// Gets the color of a <see cref="MoveType"/>, based on mainline sprite colors.
    /// </summary>
    /// <param name="type">Type to get the color of.</param>
    /// <returns>SKColor of the type.</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static SKColor GetTypeSpriteColor(byte type)
        => ((MoveType)type).GetTypeSpriteColor();

    /// <summary>
    /// Gets the color of a <see cref="MoveType"/>, based on mainline sprite colors.
    /// </summary>
    /// <param name="type">Type to get the color of.</param>
    /// <returns>SKColor of the type.</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static SKColor GetTypeSpriteColorChampions(byte type)
        => ((MoveType)type).GetTypeSpriteColorChampions();

    extension(MoveType type)
    {
        /// <inheritdoc cref="GetTypeSpriteColor(byte)"/>
        public SKColor GetTypeSpriteColor() => type switch
        {
            Normal   => new SKColor((byte)(159), (byte)(161), (byte)(159)),
            Fighting => new SKColor((byte)(255), (byte)(128), (byte)(000)),
            Flying   => new SKColor((byte)(129), (byte)(185), (byte)(239)),
            Poison   => new SKColor((byte)(143), (byte)(065), (byte)(203)),
            Ground   => new SKColor((byte)(145), (byte)(081), (byte)(033)),
            Rock     => new SKColor((byte)(175), (byte)(169), (byte)(129)),
            Bug      => new SKColor((byte)(145), (byte)(161), (byte)(025)),
            Ghost    => new SKColor((byte)(112), (byte)(065), (byte)(112)),
            Steel    => new SKColor((byte)(096), (byte)(161), (byte)(184)),
            Fire     => new SKColor((byte)(230), (byte)(040), (byte)(041)),
            Water    => new SKColor((byte)(041), (byte)(128), (byte)(239)),
            Grass    => new SKColor((byte)(063), (byte)(161), (byte)(041)),
            Electric => new SKColor((byte)(250), (byte)(192), (byte)(000)),
            Psychic  => new SKColor((byte)(239), (byte)(065), (byte)(121)),
            Ice      => new SKColor((byte)(063), (byte)(216), (byte)(255)),
            Dragon   => new SKColor((byte)(080), (byte)(097), (byte)(225)),
            Dark     => new SKColor((byte)(080), (byte)(065), (byte)(063)),
            Fairy    => new SKColor((byte)(239), (byte)(113), (byte)(239)),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };

        /// <inheritdoc cref="GetTypeSpriteColorChampions(byte)"/>
        public SKColor GetTypeSpriteColorChampions() => type switch
        {
            Normal =>   new SKColor((byte)(0x99), (byte)(0x99), (byte)(0x99)),
            Fighting => new SKColor((byte)(0xff), (byte)(0xa2), (byte)(0x02)),
            Flying =>   new SKColor((byte)(0x95), (byte)(0xc9), (byte)(0xff)),
            Poison =>   new SKColor((byte)(0x99), (byte)(0x4d), (byte)(0xcf)),
            Ground =>   new SKColor((byte)(0xab), (byte)(0x79), (byte)(0x39)),
            Rock =>     new SKColor((byte)(0xbc), (byte)(0xb8), (byte)(0x89)),
            Bug =>      new SKColor((byte)(0x9f), (byte)(0xa4), (byte)(0x24)),
            Ghost =>    new SKColor((byte)(0x6e), (byte)(0x45), (byte)(0x70)),
            Steel =>    new SKColor((byte)(0x6a), (byte)(0xae), (byte)(0xd3)),
            Fire =>     new SKColor((byte)(0xff), (byte)(0x61), (byte)(0x2c)),
            Water =>    new SKColor((byte)(0x29), (byte)(0x92), (byte)(0xff)),
            Grass =>    new SKColor((byte)(0x42), (byte)(0xbf), (byte)(0x24)),
            Electric => new SKColor((byte)(0xff), (byte)(0xdb), (byte)(0x00)),
            Psychic =>  new SKColor((byte)(0xff), (byte)(0x63), (byte)(0x7f)),
            Ice =>      new SKColor((byte)(0x42), (byte)(0xd8), (byte)(0xff)),
            Dragon =>   new SKColor((byte)(0x54), (byte)(0x62), (byte)(0xd6)),
            Dark =>     new SKColor((byte)(0x4f), (byte)(0x47), (byte)(0x47)),
            Fairy =>    new SKColor((byte)(0xff), (byte)(0xb1), (byte)(0xff)),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }

    /// <summary>
    /// SKColor to show for a <see cref="MoveType"/> of <see cref="TeraTypeUtil.Stellar"/>.
    /// </summary>
    public static SKColor Stellar => new SKColor(0xFF, 0xFF, 0xE0);

    /// <summary>
    /// Gets the color of a <see cref="MoveType"/> for a Tera sprite.
    /// </summary>
    public static SKColor GetTeraSpriteColor(byte elementalType)
    {
        if (elementalType == TeraTypeUtil.Stellar)
            return Stellar;
        return GetTypeSpriteColor(elementalType);
    }
}
