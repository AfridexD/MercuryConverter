using System.Text;
using System.Text.RegularExpressions;

namespace Mercury.Converter.Core;

/// <summary>
/// Parses the "advanced" FFmpeg options a user can type in a preset, such as "-b:v 2M -r 30".
/// Only a short list of harmless options is allowed. Anything else is rejected, so a preset can't
/// read other files, write to another location, or run another program.
/// </summary>
public static partial class FfmpegOptions
{
    private static readonly Dictionary<string, Func<string, bool>> Allowed = new(StringComparer.Ordinal)
    {
        ["-b:v"] = value => BitratePattern().IsMatch(value),
        ["-b:a"] = value => BitratePattern().IsMatch(value),
        ["-r"] = value => FrameRatePattern().IsMatch(value),
        ["-g"] = value => UnsignedPattern().IsMatch(value),
        ["-ar"] = value => UnsignedPattern().IsMatch(value),
        ["-ac"] = value => value is "1" or "2",
        ["-preset"] = value => value is "ultrafast" or "superfast" or "veryfast" or "faster" or "fast"
            or "medium" or "slow" or "slower" or "veryslow",
        ["-tune"] = value => value is "film" or "animation" or "grain" or "stillimage" or "fastdecode" or "zerolatency",
        ["-ss"] = value => TimePattern().IsMatch(value),
        ["-t"] = value => TimePattern().IsMatch(value),
        ["-af"] = value => AudioFilterPattern().IsMatch(value),
    };

    /// <summary>Returns the option tokens, ready to add to an argument list. Throws if anything is not allowed.</summary>
    public static IReadOnlyList<string> Parse(string text)
    {
        var tokens = Tokenize(text);
        var result = new List<string>();

        for (var index = 0; index < tokens.Count; index += 2)
        {
            var option = tokens[index];
            if (!Allowed.TryGetValue(option, out var isValid))
            {
                throw new ArgumentException(
                    $"The FFmpeg option '{option}' is not allowed. Allowed options: {string.Join(", ", Allowed.Keys)}.");
            }

            if (index + 1 >= tokens.Count)
            {
                throw new ArgumentException($"The FFmpeg option '{option}' needs a value.");
            }

            var value = tokens[index + 1];
            if (value.StartsWith('-') || !isValid(value))
            {
                throw new ArgumentException($"'{value}' is not a valid value for '{option}'.");
            }

            result.Add(option);
            result.Add(value);
        }

        return result;
    }

    /// <summary>Splits on spaces. Double quotes keep spaces inside one value.</summary>
    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var character in text)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (inQuotes)
        {
            throw new ArgumentException("A quote is missing its closing quote.");
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    [GeneratedRegex(@"^\d{1,5}[kKmM]?$")]
    private static partial Regex BitratePattern();

    [GeneratedRegex(@"^\d{1,3}(\.\d{1,3})?$")]
    private static partial Regex FrameRatePattern();

    [GeneratedRegex(@"^\d{1,6}$")]
    private static partial Regex UnsignedPattern();

    [GeneratedRegex(@"^\d{1,2}(:\d{1,2}){0,2}(\.\d{1,3})?$")]
    private static partial Regex TimePattern();

    // Letters, digits, and simple punctuation only. Semicolons and brackets are not allowed, which keeps the filter graph simple.
    [GeneratedRegex(@"^[A-Za-z0-9_=:.,/\- ]{1,200}$")]
    private static partial Regex AudioFilterPattern();
}
