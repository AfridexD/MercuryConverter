#if DEBUG
using System.Globalization;

namespace Mercury.Converter.App.ViewModels;

/// <summary>For testing only: with MERCURY_TEST_TIMING=&lt;file&gt;, writes how long window refreshes take (count, average, worst).</summary>
internal static class RefreshTiming
{
    private static readonly string? Path = Environment.GetEnvironmentVariable("MERCURY_TEST_TIMING");
    private static int count;
    private static double total;
    private static double worst;
    private static long lastWrite;

    public static void Record(double milliseconds, int files)
    {
        if (string.IsNullOrEmpty(Path))
        {
            return;
        }

        count++;
        total += milliseconds;
        worst = Math.Max(worst, milliseconds);
        if (Environment.TickCount64 - lastWrite < 5000)
        {
            return;
        }

        lastWrite = Environment.TickCount64;
        File.AppendAllText(Path, string.Create(CultureInfo.InvariantCulture, $"files={files} refreshes={count} avg={total / count:0.0}ms worst={worst:0.0}ms\n"));
        count = 0;
        total = 0;
        worst = 0;
    }
}
#endif
