using Xunit;
using Mercury.Converter.Core;

namespace Mercury.Converter.Core.Tests;

/// <summary>Very large batches: how many photos run at once, pausing, and remembering the list.</summary>
public sealed class LargeBatchTests
{
    private const long GiB = 1024L * 1024 * 1024;

    private static ResourceSnapshot Machine(int cores = 12, double busy = 0, long freeGiB = 24) =>
        new(cores, busy, freeGiB * GiB, 32 * GiB);

    private sealed class FakeSampler(ResourceSnapshot snapshot) : IResourceSampler
    {
        public ResourceSnapshot Sample() => snapshot;
    }

    [Fact]
    public void Our_own_running_photos_do_not_count_as_the_computer_being_busy()
    {
        // Ten photos running keep ten of twelve cores busy. That is our own work, so more photos may still start.
        var busyWithOurOwnWork = Machine(12, busy: 83);

        var withoutCredit = ResourceGovernor.Decide(busyWithOurOwnWork, PerformanceMode.Smart, MediaKind.Image);
        var withCredit = ResourceGovernor.Decide(busyWithOurOwnWork, PerformanceMode.Smart, MediaKind.Image, runningJobs: 10);

        Assert.True(withoutCredit.MaxParallelJobs <= 2);
        Assert.Equal(10, withCredit.MaxParallelJobs);
    }

    [Fact]
    public void Someone_elses_work_still_makes_photos_back_off()
    {
        // Ten of twelve cores are busy and none of that is ours.
        var budget = ResourceGovernor.Decide(Machine(12, busy: 83), PerformanceMode.Smart, MediaKind.Image, runningJobs: 0);

        Assert.True(budget.MaxParallelJobs <= 2);
    }

    [Fact]
    public void Videos_are_not_given_the_same_credit_because_one_video_uses_many_cores()
    {
        var withCredit = ResourceGovernor.Decide(Machine(12, busy: 90), PerformanceMode.Smart, MediaKind.Video, runningJobs: 1);

        Assert.Equal(1, withCredit.MaxParallelJobs);
    }

    [Fact]
    public void A_big_processor_runs_more_than_eight_photos_at_once_but_never_more_than_sixteen()
    {
        var big = ResourceGovernor.Decide(new ResourceSnapshot(32, 0, 64 * GiB, 64 * GiB), PerformanceMode.Maximum, MediaKind.Image);

        Assert.Equal(16, big.MaxParallelJobs);
        Assert.Equal(ResourceGovernor.MaxParallelPhotos, big.MaxParallelJobs);
    }

    [Fact]
    public void Small_photos_need_less_memory_so_more_of_them_fit()
    {
        var snapshot = Machine(16, freeGiB: 6);

        var small = ResourceGovernor.Decide(snapshot, PerformanceMode.Maximum, MediaKind.Image, sizeHint: 2 * 1024 * 1024);
        var large = ResourceGovernor.Decide(snapshot, PerformanceMode.Maximum, MediaKind.Image, sizeHint: 40L * 1024 * 1024);

        Assert.True(small.MaxParallelJobs > large.MaxParallelJobs);
    }

    [Fact]
    public async Task Pausing_stops_new_jobs_and_resuming_carries_on()
    {
        var scheduler = new ConversionScheduler(new FakeSampler(Machine()), PerformanceMode.Smart);
        var gate = new PauseGate();
        var items = Enumerable.Range(0, 30).Select(i => (i, MediaKind.Image, 0L)).ToList();
        var started = 0;
        var firstFew = new TaskCompletionSource();

        var run = scheduler.RunAsync(items, async (_, _, _) =>
        {
            if (Interlocked.Increment(ref started) == 3)
            {
                gate.Pause();
                firstFew.TrySetResult();
            }

            await Task.Delay(30);
        }, CancellationToken.None, gate);

        await firstFew.Task;
        await Task.Delay(300);

        var whilePaused = Volatile.Read(ref started);
        Assert.False(run.IsCompleted);
        Assert.True(whilePaused < 30);

        // Nothing more starts while paused.
        await Task.Delay(200);
        Assert.Equal(whilePaused, Volatile.Read(ref started));

        gate.Resume();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(30, started);
    }

    [Fact]
    public async Task Cancelling_while_paused_ends_the_run()
    {
        var scheduler = new ConversionScheduler(new FakeSampler(Machine()), PerformanceMode.Smart);
        var gate = new PauseGate();
        gate.Pause();
        using var cancel = new CancellationTokenSource();
        var items = Enumerable.Range(0, 5).Select(i => (i, MediaKind.Image, 0L)).ToList();
        var started = 0;

        var run = scheduler.RunAsync(items, (_, _, _) =>
        {
            Interlocked.Increment(ref started);
            return Task.CompletedTask;
        }, cancel.Token, gate);

        await Task.Delay(100);
        cancel.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, started);
    }

    [Fact]
    public void The_remembered_list_comes_back_and_ignores_damage()
    {
        var folder = Path.Combine(Path.GetTempPath(), "mercury-queue-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(folder, "queue.json");
        try
        {
            var store = new QueueStore(file);
            Assert.Empty(store.Load());

            store.Save([new SavedFile(@"C:\Photos\a.jpg", @"C:\Photos"), new SavedFile(@"C:\Photos\b.jpg", null)]);
            var loaded = store.Load();
            Assert.Equal(2, loaded.Count);
            Assert.Equal(@"C:\Photos", loaded[0].Root);
            Assert.Null(loaded[1].Root);

            // A damaged file means nothing comes back, and nothing crashes.
            File.WriteAllText(file, "{ not json");
            Assert.Empty(store.Load());

            // An empty list removes the file.
            store.Save([new SavedFile(@"C:\Photos\a.jpg", null)]);
            store.Save([]);
            Assert.False(File.Exists(file));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void Paths_that_are_not_full_paths_are_not_brought_back()
    {
        var folder = Path.Combine(Path.GetTempPath(), "mercury-queue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "queue.json");
        try
        {
            File.WriteAllText(file, """[{"Path":"relative\\a.jpg","Root":null},{"Path":"","Root":null},{"Path":"C:\\ok\\b.jpg","Root":null}]""");

            var loaded = new QueueStore(file).Load();

            Assert.Single(loaded);
            Assert.Equal(@"C:\ok\b.jpg", loaded[0].Path);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
