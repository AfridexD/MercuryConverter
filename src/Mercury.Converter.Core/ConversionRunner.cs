using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Mercury.Converter.Core;

public sealed record ConversionResult(
    bool Succeeded,
    string? OutputPath,
    long InputBytes,
    long OutputBytes,
    string? Warning,
    string? Error,
    int? AdjustedQuality = null,
    double? PartialSeconds = null)
{
    public bool WasCancelled => Error == ConversionRunner.CancelledMessage;
}

/// <summary>What a conversion has done so far. Read after a failure to see how far it got.</summary>
internal sealed class RunState
{
    /// <summary>The furthest point in the media, in seconds, that was converted before an error.</summary>
    public double LastSeconds { get; set; }

    /// <summary>Set only when the failure came after real progress, so a "save the part before it" option makes sense.</summary>
    public double? PartialSeconds => LastSeconds >= 1.0 ? LastSeconds : null;
}

/// <summary>Runs a conversion with cancellation, progress, safe output handling, and optional size control.</summary>
public sealed class ConversionRunner(ToolLocator tools)
{
    public const string CancelledMessage = "Cancelled.";

    /// <summary>How much lower each retry's quality is, when the output would be larger than the original.</summary>
    private const int QualityStep = 15;

    private const int MinimumQuality = 10;

    /// <param name="request">What to convert.</param>
    /// <param name="outputPath">Wanted output path. A free name is chosen automatically if it already exists.</param>
    /// <param name="durationSeconds">Media length, if known. Used for the progress percentage.</param>
    /// <param name="progress">Receives 0.0 to 1.0 when the duration is known.</param>
    /// <param name="keepSmallerThanOriginal">
    /// When true, a lossy result that comes out larger than the original is retried at lower quality.
    /// </param>
    public async Task<ConversionResult> RunAsync(
        ConversionRequest request,
        string outputPath,
        double? durationSeconds = null,
        IProgress<double>? progress = null,
        bool keepSmallerThanOriginal = true,
        CancellationToken cancellationToken = default)
    {
        var inputBytes = new FileInfo(request.InputPath).Length;
        var finalPath = SafeOutput.UniquePath(outputPath);
        var lossy = Targets.Get(request.Target).Lossy;
        var qualities = keepSmallerThanOriginal && lossy
            ? SizeAttempts(request.Quality)
            : [QualityMapper.Clamp(request.Quality)];

        var state = new RunState();
        try
        {
            for (var attempt = 0; attempt < qualities.Count; attempt++)
            {
                var quality = qualities[attempt];
                var temp = SafeOutput.TempPathFor(finalPath);
                var committed = false;
                try
                {
                    var error = await ConvertToTempAsync(request with { Quality = quality }, temp, durationSeconds, progress, state, cancellationToken);
                    if (error is not null)
                    {
                        return Fail(error, inputBytes, state.PartialSeconds);
                    }

                    var outputBytes = new FileInfo(temp).Length;
                    var isLast = attempt == qualities.Count - 1;
                    if (outputBytes < inputBytes || isLast || !lossy || !keepSmallerThanOriginal)
                    {
                        SafeOutput.Commit(temp, finalPath);
                        committed = true;
                        return new ConversionResult(
                            Succeeded: true,
                            OutputPath: finalPath,
                            InputBytes: inputBytes,
                            OutputBytes: outputBytes,
                            Warning: SizeWarning(inputBytes, outputBytes, quality, lossy, keepSmallerThanOriginal),
                            Error: null,
                            AdjustedQuality: quality == QualityMapper.Clamp(request.Quality) ? null : quality);
                    }
                }
                finally
                {
                    if (!committed)
                    {
                        SafeOutput.TryDelete(temp);
                    }
                }
            }

            return Fail("No output was produced.", inputBytes);
        }
        catch (OperationCanceledException)
        {
            return Fail(CancelledMessage, inputBytes);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            return Fail(ex.Message, inputBytes);
        }
    }

    /// <summary>
    /// Quality values to try, from the requested one downward. Stops early once the value would drop below the floor.
    /// </summary>
    public static IReadOnlyList<int> SizeAttempts(int requested)
    {
        var start = QualityMapper.Clamp(requested);
        var attempts = new List<int> { start };
        for (var step = 1; step <= 3; step++)
        {
            var next = Math.Max(MinimumQuality, start - QualityStep * step);
            if (next != attempts[^1])
            {
                attempts.Add(next);
            }
        }

        return attempts;
    }

    /// <summary>
    /// Plans and runs one conversion into <paramref name="temp"/>. Returns an error message, or null on success.
    /// If a graphics-card run fails, it is tried once more in software, so a driver problem never blocks a conversion.
    /// </summary>
    private async Task<string?> ConvertToTempAsync(
        ConversionRequest request,
        string temp,
        double? durationSeconds,
        IProgress<double>? progress,
        RunState state,
        CancellationToken cancellationToken)
    {
        var error = await ConvertOnceAsync(request, temp, durationSeconds, progress, state, cancellationToken);

        var exec = request.Execution ?? ExecutionOptions.Default;
        var usesFfmpegVideo = Targets.Get(request.Target).Kind is MediaKind.Video or MediaKind.Animation;
        if (error is not null && exec.UsesHardware && usesFfmpegVideo)
        {
            SafeOutput.TryDelete(temp);
            state.LastSeconds = 0;
            error = await ConvertOnceAsync(
                request with { Execution = exec.SoftwareOnly() }, temp, durationSeconds, progress, state, cancellationToken);
        }

        return error;
    }

    private async Task<string?> ConvertOnceAsync(
        ConversionRequest request,
        string temp,
        double? durationSeconds,
        IProgress<double>? progress,
        RunState state,
        CancellationToken cancellationToken)
    {
        // Plan straight to the temp file. The final step writes there, and earlier steps use their own temp files.
        var plan = CommandBuilder.Build(request, temp, Path.GetTempPath());
        var exec = request.Execution ?? ExecutionOptions.Default;
        try
        {
            var count = plan.Steps.Count;
            progress?.Report(0.02);

            for (var index = 0; index < count; index++)
            {
                // Each step fills its own share of the bar, so a two-step conversion never jumps back.
                var share = new SharedProgress(progress, index / (double)count, 1.0 / count);
                var stepDuration = index == count - 1 ? durationSeconds : null;
                var error = await RunStepAsync(plan.Steps[index], exec, stepDuration, share, state, cancellationToken);
                if (error is not null)
                {
                    return error;
                }

                share.Report(1.0);
            }

            if (!File.Exists(temp) || new FileInfo(temp).Length == 0)
            {
                return "The converter finished without writing a file.";
            }

            return null;
        }
        finally
        {
            foreach (var intermediate in plan.IntermediateFiles)
            {
                SafeOutput.TryDelete(intermediate);
            }
        }
    }

    /// <summary>Maps a step's own 0 to 1 progress onto its share of the whole conversion.</summary>
    private sealed class SharedProgress(IProgress<double>? inner, double offset, double scale) : IProgress<double>
    {
        public void Report(double value) => inner?.Report(Math.Clamp(offset + value * scale, 0, 1));
    }

    private static ConversionResult Fail(string message, long inputBytes, double? partialSeconds = null) =>
        new(false, null, inputBytes, 0, null, message, PartialSeconds: partialSeconds);

    private static string? SizeWarning(long inputBytes, long outputBytes, int quality, bool lossy, bool keepSmaller)
    {
        if (!lossy || inputBytes <= 0 || outputBytes <= inputBytes)
        {
            return null;
        }

        var percent = (int)Math.Round((outputBytes - inputBytes) * 100.0 / inputBytes);
        return keepSmaller
            ? $"Even at quality {quality}, the new file is {percent}% larger than the original. Keep the original, or choose a smaller size."
            : $"The new file is {percent}% larger than the original.";
    }

    /// <summary>Runs one tool. Returns an error message, or null on success.</summary>
    private async Task<string?> RunStepAsync(
        ToolCommand step,
        ExecutionOptions exec,
        double? durationSeconds,
        IProgress<double>? progress,
        RunState state,
        CancellationToken cancellationToken)
    {
        var fileName = tools.Locate(step.Tool);
        if (fileName is null)
        {
            return $"The bundled tool '{step.Tool}' is missing. Reinstall Mercury Converter.";
        }

        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in step.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        TrySetPriority(process, exec.Priority);

        // Stop the whole process tree on cancel so no encoder keeps the temp file open.
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // The process already exited.
            }
        });

        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        while (await process.StandardOutput.ReadLineAsync(CancellationToken.None) is { } line)
        {
            if (!TryParseOutTime(line, out var seconds))
            {
                continue;
            }

            // Remember how far it got, even without a known length. If the file turns out to be damaged, this
            // is the point up to which a partial result can be saved.
            state.LastSeconds = Math.Max(state.LastSeconds, seconds);

            if (durationSeconds is > 0 && progress is not null)
            {
                progress.Report(Math.Clamp(seconds / durationSeconds.Value, 0, 1));
            }
        }

        await process.WaitForExitAsync(CancellationToken.None);
        var stderr = await stderrTask;

        cancellationToken.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
        {
            return $"{step.Tool} failed (exit code {process.ExitCode}). {LastLines(stderr)}";
        }

        return null;
    }

    /// <summary>Lowers or keeps the Windows priority of a tool. Failing to do so is harmless.</summary>
    private static void TrySetPriority(Process process, ProcessPriorityClass priority)
    {
        if (priority == ProcessPriorityClass.Normal)
        {
            return;
        }

        try
        {
            process.PriorityClass = priority;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The process may have already finished, or Windows may refuse. Either way the conversion goes on.
        }
    }

    /// <summary>Parses `out_time_us=1234567` lines that FFmpeg prints with `-progress pipe:1`.</summary>
    private static bool TryParseOutTime(string line, out double seconds)
    {
        seconds = 0;
        const string prefix = "out_time_us=";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (!long.TryParse(line.AsSpan(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds)
            || microseconds < 0)
        {
            return false;
        }

        seconds = microseconds / 1_000_000.0;
        return true;
    }

    private static string LastLines(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? string.Empty : string.Join(" ", lines.TakeLast(3));
    }
}
