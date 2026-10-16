namespace Mercury.Converter.Core;

/// <summary>
/// Optional changes applied during conversion. Size limits only ever shrink an image or video,
/// so a small file is never enlarged. Percentage scaling is the one exception.
/// </summary>
public sealed record Transform(
    int? ScalePercent = null,
    int? MaxWidth = null,
    int? MaxHeight = null,
    int Rotation = 0,
    bool FlipHorizontal = false,
    bool FlipVertical = false)
{
    public static readonly Transform None = new();

    public bool IsNone =>
        ScalePercent is null && MaxWidth is null && MaxHeight is null
        && Rotation == 0 && !FlipHorizontal && !FlipVertical;

    public bool IsValid =>
        Rotation is 0 or 90 or 180 or 270
        && (ScalePercent is null or >= 1 and <= 400)
        && (MaxWidth is null or > 0)
        && (MaxHeight is null or > 0);

    /// <summary>Resize by a percentage, such as 50 for half size.</summary>
    public static Transform Percent(int percent) => new(ScalePercent: percent);

    /// <summary>Limit the height (for example 1080 or 720), keeping the shape.</summary>
    public static Transform FitHeight(int height) => new(MaxHeight: height);
}
