using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Mercury.Converter.Core;

/// <summary>How much of the computer a conversion may use.</summary>
public enum PerformanceMode
{
    /// <summary>Uses more of the computer when it is idle, and backs off when you are busy with something else.</summary>
    Smart,

    /// <summary>Uses at most about half of what is free, at low priority. Best while you work or play a game.</summary>
    Gentle,

    /// <summary>Uses every core at normal priority.</summary>
    Maximum,
}

/// <summary>A reading of how busy the computer is right now.</summary>
public sealed record ResourceSnapshot(
    int LogicalCores,
    double CpuBusyPercent,
    long AvailableMemoryBytes,
    long TotalMemoryBytes);

/// <summary>What the governor allows. <see cref="ThreadsPerJob"/> is what each running job should use.</summary>
public sealed record ResourceBudget(
    int TotalThreads,
    int MaxParallelJobs,
    ProcessPriorityClass Priority)
{
    public int ThreadsPerJob => Math.Max(1, TotalThreads / Math.Max(1, MaxParallelJobs));
}

public interface IResourceSampler
{
    ResourceSnapshot Sample();
}

/// <summary>Decides how many threads and parallel jobs to use, from a snapshot of the computer. Pure and easy to test.</summary>
public static class ResourceGovernor
{
    private const long GiB = 1024L * 1024 * 1024;

    /// <summary>Memory kept free for Windows and other programs, whatever the mode.</summary>
    private const long MemoryReserve = 3 * GiB / 2;

    public static ResourceBudget Decide(ResourceSnapshot snapshot, PerformanceMode mode, MediaKind kind)
    {
        var cores = Math.Max(1, snapshot.LogicalCores);
        var busy = Math.Clamp(snapshot.CpuBusyPercent, 0, 100);
        var idleCores = cores * (1 - busy / 100.0);

        var threads = mode switch
        {
            PerformanceMode.Maximum => cores,

            // Half of what is free, and never more than half the machine.
            PerformanceMode.Gentle => Math.Clamp((int)Math.Floor(idleCores / 2), 1, Math.Max(1, cores / 2)),

            // Everything that is free, minus a little so the computer stays responsive.
            _ => Math.Clamp((int)Math.Floor(idleCores) - Reserve(cores), cores >= 4 ? 2 : 1, cores),
        };

        // Each running job needs memory. Video and large images need the most.
        var perJob = kind switch
        {
            MediaKind.Video => GiB,
            MediaKind.Animation => GiB / 2,
            MediaKind.Image => GiB / 2,
            _ => GiB / 8,
        };

        var spareMemory = snapshot.AvailableMemoryBytes - MemoryReserve;
        var memoryJobs = (int)Math.Clamp(spareMemory / perJob, 1, 16);

        if (snapshot.AvailableMemoryBytes < 2 * GiB)
        {
            threads = Math.Min(threads, 2);
        }

        var wanted = kind switch
        {
            // One heavy video already uses many threads well. Two at once only help on big processors.
            MediaKind.Video => threads >= 10 ? 2 : 1,
            _ => Math.Clamp(threads / 2, 1, 4),
        };

        if (mode == PerformanceMode.Gentle)
        {
            wanted = Math.Min(wanted, 2);
        }

        var parallel = Math.Max(1, Math.Min(wanted, Math.Min(memoryJobs, threads)));

        var priority = mode switch
        {
            PerformanceMode.Gentle => ProcessPriorityClass.BelowNormal,
            PerformanceMode.Maximum => ProcessPriorityClass.Normal,
            _ => busy >= 60 ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal,
        };

        return new ResourceBudget(threads, parallel, priority);
    }

    /// <summary>Cores left alone in Smart mode, so windows and games stay smooth.</summary>
    private static int Reserve(int cores) => cores >= 8 ? 2 : 1;
}

/// <summary>Reads CPU and memory use from Windows.</summary>
public sealed partial class SystemResourceSampler : IResourceSampler
{
    /// <summary>Readings closer together than this are too short to measure CPU use, so the last one is reused.</summary>
    private const long MinimumIntervalMilliseconds = 300;

    private readonly object gate = new();
    private (ulong Idle, ulong Total)? previous;
    private ResourceSnapshot? last;
    private long lastTick;

    public ResourceSnapshot Sample()
    {
        lock (gate)
        {
            var tick = Environment.TickCount64;
            if (last is not null && tick - lastTick < MinimumIntervalMilliseconds)
            {
                return last;
            }

            var first = previous ?? ReadCpuTimes();
            if (previous is null)
            {
                // Two readings are needed to measure use. Wait a moment for the second.
                Thread.Sleep((int)MinimumIntervalMilliseconds);
            }

            var now = ReadCpuTimes();
            var idle = now.Idle - first.Idle;
            var total = now.Total - first.Total;
            var busy = total == 0 ? 0 : 100.0 * (1 - (double)idle / total);
            previous = now;

            var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            _ = GlobalMemoryStatusEx(ref memory);

            last = new ResourceSnapshot(
                Environment.ProcessorCount,
                Math.Clamp(busy, 0, 100),
                (long)memory.AvailablePhysical,
                (long)memory.TotalPhysical);
            lastTick = Environment.TickCount64;
            return last;
        }
    }

    private static (ulong Idle, ulong Total) ReadCpuTimes()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return (0, 0);
        }

        // Kernel time already includes idle time.
        return (ToUInt64(idle), ToUInt64(kernel) + ToUInt64(user));
    }

    private static ulong ToUInt64(FileTime time) => ((ulong)time.High << 32) | time.Low;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatus status);
}

/// <summary>
/// Runs a list of jobs, a few at a time. Before each job starts it asks the governor how much of the computer
/// is free, so a busy computer gets one gentle job and an idle one gets several fast jobs.
/// </summary>
public sealed class ConversionScheduler(IResourceSampler sampler, PerformanceMode mode)
{
    /// <summary>Runs every item. Cancelling stops new jobs from starting and passes the token to running ones.</summary>
    public async Task RunAsync<T>(
        IReadOnlyList<(T Item, MediaKind Kind)> items,
        Func<T, ResourceBudget, CancellationToken, Task> runItem,
        CancellationToken cancellationToken)
    {
        var queue = new Queue<(T Item, MediaKind Kind)>(items);
        var running = new List<Task>();
        var failures = new List<Exception>();

        while (queue.Count > 0 || running.Count > 0)
        {
            running.RemoveAll(task => task.IsCompleted);

            if (queue.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                var next = queue.Peek();
                var budget = ResourceGovernor.Decide(sampler.Sample(), mode, next.Kind);

                if (running.Count < budget.MaxParallelJobs)
                {
                    queue.Dequeue();
                    running.Add(RunOne(next.Item, budget, runItem, failures, cancellationToken));
                    continue;
                }
            }
            else if (queue.Count > 0)
            {
                // Cancelled: the remaining jobs never start.
                queue.Clear();
            }

            if (running.Count > 0)
            {
                await Task.WhenAny(running);
            }
        }

        if (failures.Count > 0)
        {
            throw new AggregateException(failures);
        }
    }

    private static async Task RunOne<T>(
        T item,
        ResourceBudget budget,
        Func<T, ResourceBudget, CancellationToken, Task> runItem,
        List<Exception> failures,
        CancellationToken cancellationToken)
    {
        try
        {
            await runItem(item, budget, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // A cancelled job is not a failure.
        }
        catch (Exception ex)
        {
            lock (failures)
            {
                failures.Add(ex);
            }
        }
    }
}
