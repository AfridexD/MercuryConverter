namespace Mercury.Converter.Core;

/// <summary>A graphics-card video encoder family that FFmpeg can use for H.264.</summary>
public enum HardwareEncoder
{
    None,
    Nvenc,
    Amf,
    Qsv,
}

/// <summary>
/// How a conversion should run on this computer. These settings never change what the result looks like,
/// except that graphics-card encoders make slightly larger files than software encoding.
/// </summary>
/// <param name="Threads">Worker threads for each tool. Null lets the tool choose.</param>
/// <param name="UseHardwareDecode">Decode video on the graphics card when possible. FFmpeg falls back to software if it can't.</param>
/// <param name="Encoder">Encode H.264 on the graphics card instead of in software.</param>
/// <param name="Priority">Windows priority for the tool processes. Below normal keeps the computer responsive.</param>
public sealed record ExecutionOptions(
    int? Threads = null,
    bool UseHardwareDecode = true,
    HardwareEncoder Encoder = HardwareEncoder.None,
    System.Diagnostics.ProcessPriorityClass Priority = System.Diagnostics.ProcessPriorityClass.Normal)
{
    public static readonly ExecutionOptions Default = new();

    /// <summary>The same settings without any graphics-card use. Used to retry a failed hardware run.</summary>
    public ExecutionOptions SoftwareOnly() => this with { UseHardwareDecode = false, Encoder = HardwareEncoder.None };

    public bool UsesHardware => UseHardwareDecode || Encoder != HardwareEncoder.None;
}
