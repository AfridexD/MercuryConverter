namespace Mercury.Converter.Core;

/// <summary>How much effort the video encoder spends. More effort makes smaller files but takes longer.</summary>
public enum VideoSpeed
{
    Faster,
    Balanced,
    Slower,
}

public enum AudioChannels
{
    KeepAsIs,
    Mono,
    Stereo,
}

/// <summary>Turns the plain-language options into the safe FFmpeg options the converter accepts.</summary>
public static class ConversionOptions
{
    public static string Label(VideoSpeed speed) => speed switch
    {
        VideoSpeed.Faster => "Faster",
        VideoSpeed.Slower => "Slower",
        _ => "Balanced",
    };

    public static string Label(AudioChannels channels) => channels switch
    {
        AudioChannels.Mono => "Mono",
        AudioChannels.Stereo => "Stereo",
        _ => "Keep as is",
    };

    /// <summary>
    /// The extra FFmpeg options for these choices, or null for none. The "-preset" option only exists for the
    /// software H.264 encoder, so it is left out for other formats and for graphics-card encoding.
    /// </summary>
    public static string? FfmpegOptions(TargetFormat target, VideoSpeed speed, AudioChannels channels, bool graphicsCardEncoder)
    {
        var options = new List<string>();

        var usesX264 = target is TargetFormat.Mp4 or TargetFormat.Mkv && !graphicsCardEncoder;
        if (usesX264 && speed != VideoSpeed.Balanced)
        {
            options.Add(speed == VideoSpeed.Faster ? "-preset veryfast" : "-preset slower");
        }

        if (MediaGroups.Of(target) == MediaGroup.Audio && channels != AudioChannels.KeepAsIs)
        {
            options.Add(channels == AudioChannels.Mono ? "-ac 1" : "-ac 2");
        }

        return options.Count == 0 ? null : string.Join(' ', options);
    }
}
