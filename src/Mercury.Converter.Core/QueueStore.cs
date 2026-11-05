using System.Text.Json;

namespace Mercury.Converter.Core;

/// <summary>One file waiting to be converted, as remembered between runs.</summary>
/// <param name="Path">Where the file is.</param>
/// <param name="Root">The folder it was added from, so its folder layout can be kept. Null for files added one by one.</param>
public sealed record SavedFile(string Path, string? Root);

/// <summary>
/// Remembers the files that were not converted yet, so closing the app does not lose a long list.
/// Only paths are stored (nothing from inside the files), in the same folder as the settings.
/// A damaged file simply means nothing is brought back.
/// </summary>
public sealed class QueueStore(string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public static QueueStore Default { get; } = new(System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MercuryConverter",
        "queue.json"));

    public List<SavedFile> Load()
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return [];
            }

            using var stream = File.OpenRead(filePath);
            // A list of several hundred thousand paths is far beyond real use. Refuse anything absurd.
            if (stream.Length > 200L * 1024 * 1024)
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<SavedFile>>(stream, JsonOptions)?
                .Where(f => !string.IsNullOrWhiteSpace(f.Path) && System.IO.Path.IsPathRooted(f.Path))
                .ToList() ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private readonly object gate = new();

    /// <summary>Saves the list. An empty list removes the file.</summary>
    public void Save(IReadOnlyCollection<SavedFile> files)
    {
        try
        {
            lock (gate)
            {
                Write(files);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Remembering the list is a convenience. A failed save must never interrupt the user.
        }
    }

    private void Write(IReadOnlyCollection<SavedFile> files)
    {
        if (files.Count == 0)
        {
            File.Delete(filePath);
            return;
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(filePath)!);
        var temp = filePath + ".tmp";
        using (var stream = File.Create(temp))
        {
            JsonSerializer.Serialize(stream, files, JsonOptions);
        }

        File.Move(temp, filePath, overwrite: true);
    }
}
