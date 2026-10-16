namespace Mercury.Converter.Core;

/// <summary>
/// Writes output without leaving half-finished files behind and without changing existing files.
/// The encoder writes to a hidden temp file in the same folder. The temp file is renamed to the
/// final name only after the encoder has succeeded.
/// </summary>
public static class SafeOutput
{
    /// <summary>A hidden temp path in the destination folder. It keeps the real extension so encoders can pick a format.</summary>
    public static string TempPathFor(string finalPath)
    {
        var directory = Path.GetDirectoryName(finalPath) ?? throw new ArgumentException("Output path needs a folder.", nameof(finalPath));
        var extension = Path.GetExtension(finalPath);
        return Path.Combine(directory, $".Mercury.Converter-{Guid.NewGuid():N}{extension}");
    }

    /// <summary>
    /// Returns <paramref name="path"/> if it is free, otherwise "name (2).ext", "name (3).ext", and so on.
    /// Existing files are never overwritten.
    /// </summary>
    public static string UniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var index = 2; index < 10_000; index++)
        {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"Could not find a free file name for '{path}'.");
    }

    /// <summary>Moves a finished temp file into place. Fails rather than overwriting.</summary>
    public static void Commit(string tempPath, string finalPath)
    {
        File.Move(tempPath, finalPath, overwrite: false);
    }

    /// <summary>Deletes a temp file if it exists. Never throws, so it is safe in cleanup code.</summary>
    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Windows can briefly hold a file open after a process exits. The temp file is hidden, so it is harmless.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
