using System.Diagnostics;

namespace Mercury.Converter.Core;

/// <summary>
/// Finds out whether a graphics-card H.264 encoder really works on this computer. FFmpeg lists every encoder it
/// was built with, even ones that need hardware the computer does not have, so the only reliable check is to
/// encode one tiny test clip.
/// </summary>
public sealed class HardwareProbe(ToolLocator tools)
{
    private static readonly (HardwareEncoder Encoder, string Name)[] Candidates =
    [
        (HardwareEncoder.Nvenc, "h264_nvenc"),
        (HardwareEncoder.Amf, "h264_amf"),
        (HardwareEncoder.Qsv, "h264_qsv"),
    ];

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Returns the first graphics-card encoder that works, or None.</summary>
    public async Task<HardwareEncoder> DetectEncoderAsync(CancellationToken cancellationToken = default)
    {
        var ffmpeg = tools.Locate(Tool.FFmpeg);
        if (ffmpeg is null)
        {
            return HardwareEncoder.None;
        }

        foreach (var (encoder, name) in Candidates)
        {
            if (await WorksAsync(ffmpeg, name, cancellationToken))
            {
                return encoder;
            }
        }

        return HardwareEncoder.None;
    }

    private static async Task<bool> WorksAsync(string ffmpeg, string encoderName, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        foreach (var argument in new[]
        {
            "-hide_banner", "-nostdin", "-loglevel", "error",
            "-f", "lavfi", "-i", "color=c=black:s=256x144:d=0.3:r=10",
            "-c:v", encoderName, "-pix_fmt", "yuv420p", "-f", "null", "-",
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);

            // Read both streams so a chatty failure can't block the process.
            var drain = Task.WhenAll(process.StandardError.ReadToEndAsync(CancellationToken.None), process.StandardOutput.ReadToEndAsync(CancellationToken.None));

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                cancellationToken.ThrowIfCancellationRequested();
                return false;
            }

            await drain;
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
