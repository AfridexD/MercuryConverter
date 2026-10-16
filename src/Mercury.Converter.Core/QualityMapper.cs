namespace Mercury.Converter.Core;

/// <summary>
/// Converts one plain 1â€“100 quality slider into each encoder's native setting.
/// 100 is the best quality (largest file); 1 is the smallest file.
/// </summary>
public static class QualityMapper
{
    public const int Lowest = 1;
    public const int Highest = 100;
    public const int Balanced = 75;

    public static int Clamp(int quality) => Math.Clamp(quality, Lowest, Highest);

    /// <summary>ImageMagick `-quality` value for JPEG, WebP and AVIF.</summary>
    public static int MagickQuality(int quality) => Clamp(quality);

    /// <summary>
    /// libjxl "distance" (0 = lossless, about 1 = visually lossless, higher = smaller).
    /// </summary>
    public static double JxlDistance(int quality)
    {
        var q = Clamp(quality);
        if (q == Highest)
        {
            return 0;
        }

        return Math.Min(15.0, (Highest - q) / 10.0);
    }

    /// <summary>H.264 CRF: lower is better. Ranges from 15 (best) to 33 (smallest).</summary>
    public static int H264Crf(int quality) => (int)Math.Round(33 - Clamp(quality) * 0.18);

    /// <summary>VP9 CRF: lower is better. Ranges from 30 (best) to 50 (smallest).</summary>
    public static int Vp9Crf(int quality) => (int)Math.Round(50 - Clamp(quality) * 0.2);

    /// <summary>Palette size for GIF: 18 colors at the lowest setting, 256 at the highest.</summary>
    public static int GifColors(int quality) => (int)Math.Round(16 + Clamp(quality) * 2.4);

    /// <summary>Theora and Vorbis quality, 0 (smallest) to 10 (best).</summary>
    public static int OggQuality(int quality) => (int)Math.Round(Clamp(quality) / 10.0);

    /// <summary>MPEG-4 (AVI) quantizer: 2 is best, 31 is smallest.</summary>
    public static int Mpeg4Quantizer(int quality) => (int)Math.Round(31 - (Clamp(quality) - 1) * 29 / 99.0);

    /// <summary>AAC bitrate in kbps: 64 at the lowest setting, 256 at the highest.</summary>
    public static int AacBitrateKbps(int quality) => (int)Math.Round(64 + (Clamp(quality) - 1) * 192 / 99.0);

    /// <summary>MP3 VBR setting for LAME: 0 is best, 9 is smallest.</summary>
    public static int Mp3VbrScale(int quality) => (int)Math.Round((Highest - Clamp(quality)) / 11.0);
}
