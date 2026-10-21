using System.Globalization;

namespace Mercury.Converter.Core;

/// <summary>The external programs Mercury Converter runs. Each is bundled with the app.</summary>
public enum Tool
{
    FFmpeg,
    ImageMagick,
    CJxl,
    DJxl,
}

/// <summary>One program run with an exact argument list (no shell, so paths with spaces are safe).</summary>
public sealed record ToolCommand(Tool Tool, IReadOnlyList<string> Arguments);

/// <summary>
/// What the user asked for. The output path is final; the runner writes to a temp file first.
/// <paramref name="Changes"/> is optional resize, rotate, or flip. Null means no changes.
/// <paramref name="ExtraFfmpegArguments"/> is checked against an allowlist before use. See <see cref="FfmpegOptions"/>.
/// <paramref name="Execution"/> controls threads and graphics-card use. Null means the defaults.
/// </summary>
public sealed record ConversionRequest(
    string InputPath,
    TargetFormat Target,
    int Quality = QualityMapper.Balanced,
    bool LosslessJpegRecompression = false,
    Transform? Changes = null,
    string? ExtraFfmpegArguments = null,
    ExecutionOptions? Execution = null,
    bool StripMetadata = false);

/// <summary>Turns a request into an ordered list of tool runs. Temp files are listed for cleanup.</summary>
public sealed record ConversionPlan(
    IReadOnlyList<ToolCommand> Steps,
    IReadOnlyList<string> IntermediateFiles);

public static class CommandBuilder
{
    /// <summary>
    /// Builds the steps that turn <paramref name="request"/> into <paramref name="outputPath"/>.
    /// The output path must already carry the correct extension, because encoders pick
    /// their format from it.
    /// </summary>
    public static ConversionPlan Build(ConversionRequest request, string outputPath, string tempDirectory)
    {
        if (!InputFormats.TryGetKind(request.InputPath, out var inputKind))
        {
            throw new NotSupportedException($"Mercury Converter cannot read '{Path.GetExtension(request.InputPath)}' files yet.");
        }

        var target = Targets.Get(request.Target);
        var inputExtension = Path.GetExtension(request.InputPath).TrimStart('.').ToLowerInvariant();
        var quality = QualityMapper.Clamp(request.Quality);
        var changes = request.Changes ?? Transform.None;
        if (!changes.IsValid)
        {
            throw new ArgumentException("Invalid resize, rotate, or flip settings.", nameof(request));
        }

        var exec = request.Execution ?? ExecutionOptions.Default;
        var intermediates = new List<string>();
        var steps = new List<ToolCommand>();

        if (request.Target == TargetFormat.JpegXl)
        {
            BuildJpegXl(request, inputExtension, inputKind, quality, changes, exec, outputPath, tempDirectory, intermediates, steps);
        }
        else if (request.Target == TargetFormat.Pdf)
        {
            if (inputKind is MediaKind.Video or MediaKind.Audio)
            {
                throw new NotSupportedException("Video and audio files cannot be converted to PDF.");
            }

            steps.Add(ImageStep(request.InputPath, TargetFormat.Pdf, quality, changes, exec, outputPath, request.StripMetadata));
        }
        else if (target.Kind == MediaKind.Image)
        {
            if (inputKind is MediaKind.Video or MediaKind.Audio)
            {
                throw new NotSupportedException("Video and audio files cannot be converted to still images yet.");
            }

            steps.Add(ImageStep(request.InputPath, request.Target, quality, changes, exec, outputPath, request.StripMetadata));
        }
        else if (request.Target == TargetFormat.WebPAnimated)
        {
            if (inputKind is MediaKind.Image or MediaKind.Audio)
            {
                throw new NotSupportedException("Only videos and animations can be converted to an animated WebP.");
            }

            steps.Add(AnimatedWebPStep(request.InputPath, quality, changes, exec, outputPath, request.StripMetadata));
        }
        else if (request.Target == TargetFormat.Gif)
        {
            steps.Add(GifStep(request.InputPath, quality, changes, exec, outputPath));
        }
        else if (target.Kind == MediaKind.Video)
        {
            if (inputKind == MediaKind.Image)
            {
                throw new NotSupportedException("Still images cannot be converted to video yet.");
            }

            steps.Add(VideoStep(request.InputPath, request.Target, quality, changes, request.ExtraFfmpegArguments, exec, outputPath, request.StripMetadata));
        }
        else
        {
            if (inputKind == MediaKind.Image)
            {
                throw new NotSupportedException("Images cannot be converted to audio.");
            }

            steps.Add(AudioStep(request.InputPath, request.Target, quality, exec, request.ExtraFfmpegArguments, outputPath, request.StripMetadata));
        }

        return new ConversionPlan(steps, intermediates);
    }

    private static void BuildJpegXl(
        ConversionRequest request,
        string inputExtension,
        MediaKind inputKind,
        int quality,
        Transform changes,
        ExecutionOptions exec,
        string outputPath,
        string tempDirectory,
        List<string> intermediates,
        List<ToolCommand> steps)
    {
        // An animated GIF becomes an animated JPEG XL. Video and audio cannot be converted.
        if (inputKind is not (MediaKind.Image or MediaKind.Animation))
        {
            throw new NotSupportedException("Only images can be converted to JPEG XL.");
        }

        var isJpeg = inputExtension is "jpg" or "jpeg" or "jfif";
        var threadArgs = exec.Threads is { } threads
            ? new[] { $"--num_threads={threads.ToString(CultureInfo.InvariantCulture)}" }
            : [];

        // Lossless JPEG recompression keeps the exact pixels, so it can't be combined with resizing or rotating.
        if (isJpeg && request.LosslessJpegRecompression && changes.IsNone && !request.StripMetadata)
        {
            // Keeps every JPEG pixel and can be turned back into the original JPEG byte-for-byte.
            steps.Add(new ToolCommand(Tool.CJxl, [request.InputPath, outputPath, "--lossless_jpeg=1", .. threadArgs]));
            return;
        }

        var distance = QualityMapper.JxlDistance(quality);
        var distanceText = distance.ToString("0.0#", CultureInfo.InvariantCulture);
        var cjxlReads = changes.IsNone && !request.StripMetadata && inputExtension is "png" or "jpg" or "jpeg" or "jfif" or "gif";

        if (cjxlReads)
        {
            steps.Add(new ToolCommand(Tool.CJxl, [request.InputPath, outputPath, "-d", distanceText, "-e", "7", .. threadArgs]));
            return;
        }

        // Any other format, or any image with changes, is first decoded to a temporary PNG, then encoded.
        var png = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.png");
        intermediates.Add(png);
        steps.Add(ImageStep(request.InputPath, TargetFormat.Png, quality, changes, exec, png, request.StripMetadata));
        steps.Add(new ToolCommand(Tool.CJxl, [png, outputPath, "-d", distanceText, "-e", "7", .. threadArgs]));
    }

    private static ToolCommand ImageStep(string input, TargetFormat target, int quality, Transform changes, ExecutionOptions exec, string output, bool strip = false)
    {
        var args = new List<string>();

        if (exec.Threads is { } threads)
        {
            args.AddRange(["-limit", "thread", threads.ToString(CultureInfo.InvariantCulture)]);
        }

        // "[0]" takes the first frame of multi-frame files such as animated GIF or PDF.
        args.AddRange([input + "[0]", "-auto-orient"]);

        // Removes location, camera and date details, and keeps the color profile so colors do not change.
        if (strip)
        {
            args.AddRange(["+profile", "!icc,*"]);
        }

        if (changes.Rotation != 0)
        {
            args.AddRange(["-rotate", changes.Rotation.ToString(CultureInfo.InvariantCulture)]);
        }

        if (changes.FlipHorizontal)
        {
            args.Add("-flop");
        }

        if (changes.FlipVertical)
        {
            args.Add("-flip");
        }

        var resize = MagickResizeGeometry(changes);
        if (resize is not null)
        {
            args.AddRange(["-resize", resize]);
        }

        if (target is TargetFormat.Jpeg or TargetFormat.WebP or TargetFormat.Avif)
        {
            var magickQuality = QualityMapper.MagickQuality(quality);

            // At 100 the WebP encoder switches to a lossless mode that is about 6 times slower and makes files bigger than
            // the original. 99 is lossy and looks the same.
            if (target == TargetFormat.WebP)
            {
                magickQuality = Math.Min(magickQuality, 99);
            }

            args.AddRange(["-quality", magickQuality.ToString(CultureInfo.InvariantCulture)]);
        }

        // Transparent pixels would turn black in formats without alpha, so flatten onto white.
        if (target is TargetFormat.Jpeg or TargetFormat.Pdf)
        {
            args.AddRange(["-background", "white", "-alpha", "remove", "-alpha", "off"]);
        }

        args.Add(output);
        return new ToolCommand(Tool.ImageMagick, args);
    }

    /// <summary>ImageMagick geometry. The ">" flag means "only shrink, never enlarge".</summary>
    private static string? MagickResizeGeometry(Transform changes)
    {
        if (changes.ScalePercent is { } percent)
        {
            return percent.ToString(CultureInfo.InvariantCulture) + "%";
        }

        if (changes.MaxWidth is null && changes.MaxHeight is null)
        {
            return null;
        }

        var width = changes.MaxWidth?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        var height = changes.MaxHeight?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        return $"{width}x{height}>";
    }

    /// <summary>FFmpeg filters for rotation, flips, and resizing. Empty when nothing is requested.</summary>
    private static List<string> FfmpegTransformFilters(Transform changes)
    {
        var filters = new List<string>();

        switch (changes.Rotation)
        {
            case 90:
                filters.Add("transpose=1");
                break;
            case 180:
                filters.Add("transpose=1");
                filters.Add("transpose=1");
                break;
            case 270:
                filters.Add("transpose=2");
                break;
        }

        if (changes.FlipHorizontal)
        {
            filters.Add("hflip");
        }

        if (changes.FlipVertical)
        {
            filters.Add("vflip");
        }

        if (changes.ScalePercent is { } percent)
        {
            var factor = (percent / 100.0).ToString("0.###", CultureInfo.InvariantCulture);
            filters.Add($"scale=trunc(iw*{factor}/2)*2:trunc(ih*{factor}/2)*2");
        }
        else if (changes.MaxWidth is not null || changes.MaxHeight is not null)
        {
            // Shrinks to fit the box and keeps the shape. The box never grows past the source size.
            var width = changes.MaxWidth?.ToString(CultureInfo.InvariantCulture) ?? "iw";
            var height = changes.MaxHeight?.ToString(CultureInfo.InvariantCulture) ?? "ih";
            filters.Add($"scale='min({width},iw)':'min({height},ih)':force_original_aspect_ratio=decrease");
        }

        return filters;
    }

    /// <summary>The start of every FFmpeg command: quiet output, hardware decoding, and thread limits.</summary>
    private static List<string> FfmpegStart(string input, ExecutionOptions exec)
    {
        // "-progress pipe:1" prints key=value lines on stdout, so the app can show a real percentage.
        var args = new List<string> { "-hide_banner", "-nostdin", "-y", "-progress", "pipe:1", "-nostats" };

        // Decoding on the graphics card is free speed: it doesn't change the picture. FFmpeg falls back to
        // software on its own when a file can't be decoded that way.
        if (exec.UseHardwareDecode)
        {
            args.AddRange(["-hwaccel", "auto"]);
        }

        // A file can name other addresses inside itself (a playlist, for example). Only local files are allowed.
        args.AddRange(["-protocol_whitelist", "file,pipe", "-i", input]);

        if (exec.Threads is { } threads)
        {
            var text = threads.ToString(CultureInfo.InvariantCulture);
            args.AddRange(["-threads", text, "-filter_threads", text, "-filter_complex_threads", text]);
        }

        return args;
    }

    /// <summary>A looping WebP picture from a video or a GIF. No sound, like a GIF, but smaller and with full colors.</summary>
    private static ToolCommand AnimatedWebPStep(string input, int quality, Transform changes, ExecutionOptions exec, string output, bool strip)
    {
        var filters = FfmpegTransformFilters(changes);
        if (changes.ScalePercent is null && changes.MaxWidth is null && changes.MaxHeight is null)
        {
            // Wide enough to look good, small enough to stay light. A size chosen by the user replaces this.
            filters.Add("scale='min(640,iw)':-2:flags=lanczos");
        }

        filters.Add("fps=15");

        var args = FfmpegStart(input, exec);
        args.AddRange(
        [
            "-vf", string.Join(",", filters),
            "-an",
            "-c:v", "libwebp_anim",
            "-lossless", "0",
            "-quality", quality.ToString(CultureInfo.InvariantCulture),
            "-compression_level", "4",
            "-loop", "0",
        ]);
        AddStripArguments(args, strip);
        args.Add(output);
        return new ToolCommand(Tool.FFmpeg, args);
    }

    private static ToolCommand GifStep(string input, int quality, Transform changes, ExecutionOptions exec, string output)
    {
        var colors = QualityMapper.GifColors(quality);

        // Build a palette from the frames, then map the frames onto it. This gives far better colors
        // than FFmpeg's default GIF encoder. Width is capped at 640 px by default, unless the user sets a size.
        var filters = FfmpegTransformFilters(changes);
        if (changes.ScalePercent is null && changes.MaxWidth is null && changes.MaxHeight is null)
        {
            filters.Add("scale='min(640,iw)':-2:flags=lanczos");
        }

        filters.Add("fps=12");
        var prefix = string.Join(",", filters);

        var graph =
            $"{prefix},split[s0][s1];" +
            $"[s0]palettegen=max_colors={colors}:stats_mode=diff[p];" +
            "[s1][p]paletteuse=dither=bayer:bayer_scale=5";

        var args = FfmpegStart(input, exec);
        args.AddRange(["-filter_complex", graph, "-loop", "0", output]);
        return new ToolCommand(Tool.FFmpeg, args);
    }

    private static ToolCommand VideoStep(
        string input,
        TargetFormat target,
        int quality,
        Transform changes,
        string? extraArguments,
        ExecutionOptions exec,
        string output,
        bool strip = false)
    {
        var args = FfmpegStart(input, exec);

        // The encoders need even width and height. This step makes odd-sized video convertible.
        var filters = FfmpegTransformFilters(changes);
        filters.Add("scale=trunc(iw/2)*2:trunc(ih/2)*2");
        args.AddRange(["-vf", string.Join(",", filters)]);

        var crf = QualityMapper.H264Crf(quality).ToString(CultureInfo.InvariantCulture);

        switch (target)
        {
            case TargetFormat.Mp4:
                args.AddRange(H264Arguments(exec.Encoder, crf));
                args.AddRange(["-movflags", "+faststart", "-c:a", "aac", "-b:a", "160k"]);
                break;

            case TargetFormat.Mkv:
                // MKV keeps every audio track and every subtitle. The "?" makes a missing kind of track fine.
                args.AddRange(["-map", "0:v:0", "-map", "0:a?", "-map", "0:s?", "-c:s", "copy"]);
                args.AddRange(H264Arguments(exec.Encoder, crf));
                args.AddRange(["-c:a", "aac", "-b:a", "160k"]);
                break;

            case TargetFormat.WebM:
                args.AddRange(
                [
                    "-c:v", "libvpx-vp9",
                    "-crf", QualityMapper.Vp9Crf(quality).ToString(CultureInfo.InvariantCulture),
                    "-b:v", "0", "-row-mt", "1",
                    "-c:a", "libopus", "-b:a", "128k",
                ]);
                break;

            case TargetFormat.Ogv:
                args.AddRange(
                [
                    "-c:v", "libtheora",
                    "-q:v", QualityMapper.OggQuality(quality).ToString(CultureInfo.InvariantCulture),
                    "-pix_fmt", "yuv420p",
                    "-c:a", "libvorbis",
                    "-q:a", QualityMapper.OggQuality(quality).ToString(CultureInfo.InvariantCulture),
                ]);
                break;

            case TargetFormat.Avi:
                args.AddRange(
                [
                    "-c:v", "mpeg4",
                    "-q:v", QualityMapper.Mpeg4Quantizer(quality).ToString(CultureInfo.InvariantCulture),
                    "-pix_fmt", "yuv420p",
                    "-c:a", "libmp3lame", "-b:a", "192k",
                ]);
                break;

            default:
                throw new NotSupportedException($"{target} is not a video format.");
        }

        AddStripArguments(args, strip);
        AddExtraArguments(args, extraArguments);

        args.Add(output);
        return new ToolCommand(Tool.FFmpeg, args);
    }

    /// <summary>Drops tags, location and chapter details from the result.</summary>
    private static void AddStripArguments(List<string> args, bool strip)
    {
        if (strip)
        {
            args.AddRange(["-map_metadata", "-1", "-map_chapters", "-1"]);
        }
    }

    /// <summary>Adds the user's extra FFmpeg options after checking them against the allowlist.</summary>
    private static void AddExtraArguments(List<string> args, string? extraArguments)
    {
        if (string.IsNullOrWhiteSpace(extraArguments))
        {
            return;
        }

        var extra = FfmpegOptions.Parse(extraArguments);

        // A user's option replaces the built-in one with the same name, so there is only one value in the command.
        for (var index = args.Count - 2; index >= 0; index--)
        {
            if (args[index].StartsWith('-') && extra.Contains(args[index]))
            {
                args.RemoveRange(index, 2);
            }
        }

        args.AddRange(extra);
    }

    /// <summary>H.264 video arguments for software (x264) or a graphics-card encoder.</summary>
    private static string[] H264Arguments(HardwareEncoder encoder, string crf) => encoder switch
    {
        HardwareEncoder.Nvenc =>
            ["-c:v", "h264_nvenc", "-preset", "p5", "-rc", "vbr", "-cq", crf, "-b:v", "0", "-pix_fmt", "yuv420p"],
        HardwareEncoder.Amf =>
            ["-c:v", "h264_amf", "-quality", "quality", "-rc", "cqp", "-qp_i", crf, "-qp_p", crf, "-pix_fmt", "yuv420p"],
        HardwareEncoder.Qsv =>
            ["-c:v", "h264_qsv", "-preset", "medium", "-global_quality", crf, "-pix_fmt", "nv12"],
        _ =>
            ["-c:v", "libx264", "-preset", "medium", "-crf", crf, "-pix_fmt", "yuv420p"],
    };

    private static ToolCommand AudioStep(string input, TargetFormat target, int quality, ExecutionOptions exec, string? extraArguments, string output, bool strip = false)
    {
        var args = FfmpegStart(input, exec with { UseHardwareDecode = false });
        args.Add("-vn");

        switch (target)
        {
            case TargetFormat.Mp3:
                args.AddRange(["-c:a", "libmp3lame", "-q:a", QualityMapper.Mp3VbrScale(quality).ToString(CultureInfo.InvariantCulture)]);
                break;
            case TargetFormat.Flac:
                args.AddRange(["-c:a", "flac"]);
                break;
            case TargetFormat.Ogg:
                args.AddRange(["-c:a", "libvorbis", "-q:a", QualityMapper.OggQuality(quality).ToString(CultureInfo.InvariantCulture)]);
                break;
            case TargetFormat.Wav:
                args.AddRange(["-c:a", "pcm_s16le"]);
                break;
            case TargetFormat.Aac:
                args.AddRange(["-c:a", "aac", "-b:a", QualityMapper.AacBitrateKbps(quality).ToString(CultureInfo.InvariantCulture) + "k"]);
                break;
            default:
                throw new NotSupportedException($"{target} is not an audio format.");
        }

        AddStripArguments(args, strip);
        AddExtraArguments(args, extraArguments);

        args.Add(output);
        return new ToolCommand(Tool.FFmpeg, args);
    }
}
