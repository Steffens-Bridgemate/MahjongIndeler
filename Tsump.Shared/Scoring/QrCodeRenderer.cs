using QRCoder;

namespace Tsump.Scoring;

/// <summary>
/// Generates the SVG markup for a QR code, with the root &lt;svg&gt; tag's hard-coded
/// width/height attributes stripped so the consumer (CSS or canvas) can size it.
/// </summary>
public static class QrCodeRenderer
{
    public static string ToSvg(string url, int pixelsPerModule = 4)
    {
        using var gen = new QRCodeGenerator();
        var data = gen.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        var renderer = new SvgQRCode(data);
        var rawSvg = renderer.GetGraphic(pixelsPerModule);

        // Strip width/height attributes from the opening <svg> tag only (not from inner
        // <rect> elements, which encode each QR module and need their dimensions intact).
        var openTag = System.Text.RegularExpressions.Regex.Match(
            rawSvg, @"<svg\b[^>]*>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!openTag.Success) return rawSvg;

        var cleaned = System.Text.RegularExpressions.Regex.Replace(
            openTag.Value, @"\s+(width|height)=""[^""]*""", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return rawSvg.Substring(0, openTag.Index)
               + cleaned
               + rawSvg.Substring(openTag.Index + openTag.Length);
    }
}
