using PKHeX.Core;
using QRCoder;
using SkiaSharp;

namespace PKHeX.Drawing.Misc;

/// <summary>
/// Provides methods for generating QR codes from various PKHeX data types.
/// </summary>
public static class QREncode
{
    // ControlText (dark) and ControlLightLight (light) approximations, RGBA.
    private static readonly byte[] DarkColor = [0x00, 0x00, 0x00, 0xFF];
    private static readonly byte[] LightColor = [0xF0, 0xF0, 0xF0, 0xFF];

    /// <summary>
    /// Generates a QR code bitmap from a <see cref="DataMysteryGift"/> object.
    /// </summary>
    public static SKBitmap GenerateQRCode(DataMysteryGift mg) => GenerateQRCode(QRMessageUtil.GetMessage(mg));

    /// <summary>
    /// Generates a QR code bitmap from a <see cref="PKM"/> object.
    /// </summary>
    public static SKBitmap GenerateQRCode(PKM pk) => GenerateQRCode(QRMessageUtil.GetMessage(pk));

    /// <summary>
    /// Generates a QR code bitmap for a Generation 7 PKM with additional options.
    /// </summary>
    public static SKBitmap GenerateQRCode7(PK7 pk7, int box = 0, int slot = 0, int copies = 1)
        => GenerateQRCode(QRMessageUtil.GetMessage(pk7, box, slot, copies), ppm: 4);

    /// <summary>
    /// Generates a QR code bitmap from a message string.
    /// </summary>
    /// <param name="msg">The message to encode in the QR code.</param>
    /// <param name="ppm">Pixels per module for the QR code graphic.</param>
    private static SKBitmap GenerateQRCode(string msg, int ppm = 4)
    {
        using var data = QRCodeGenerator.GenerateQrCode(msg, QRCodeGenerator.ECCLevel.Q);
        // PngByteQRCode has no System.Drawing dependency (unlike QRCoder's GDI QRCode type).
        var png = new PngByteQRCode(data).GetGraphic(ppm, DarkColor, LightColor, drawQuietZones: true);
        return SKBitmap.Decode(png);
    }
}
