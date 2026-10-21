using System.Diagnostics;
using Mercury.Converter.Core;
using Xunit;

namespace Mercury.Converter.Core.Tests;

public class ResourceTests
{
    private const long GiB = 1024L * 1024 * 1024;

    private static ResourceSnapshot Machine(int cores = 12, double busy = 0, long freeGiB = 24) =>
        new(cores, busy, freeGiB * GiB, 32 * GiB);

    private sealed class FakeSampler(ResourceSnapshot snapshot) : IResourceSampler
    {
        public ResourceSnapshot Sample() => snapshot;
    }

    [Fact]
    public void An_idle_computer_gets_most_cores_in_smart_mode()
    {
        var budget = ResourceGovernor.Decide(Machine(busy: 0), PerformanceMode.Smart, MediaKind.Image);

        Assert.Equal(10, budget.TotalThreads);
        Assert.Equal(ProcessPriorityClass.Normal, budget.Priority);
    }

    [Fact]
    public void A_busy_computer_gets_few_threads_and_lower_priority()
    {
        var budget = ResourceGovernor.Decide(Machine(busy: 90), PerformanceMode.Smart, MediaKind.Video);

        Assert.Equal(2, budget.TotalThreads);
        Assert.Equal(1, budget.MaxParallelJobs);
        Assert.Equal(ProcessPriorityClass.BelowNormal, budget.Priority);
    }

    [Fact]
    public void Threads_follow_how_much_of_the_cpu_is_free()
    {
        var quiet = ResourceGovernor.Decide(Machine(busy: 10), PerformanceMode.Smart, MediaKind.Video).TotalThreads;
        var moderate = ResourceGovernor.Decide(Machine(busy: 50), PerformanceMode.Smart, MediaKind.Video).TotalThreads;
        var heavy = ResourceGovernor.Decide(Machine(busy: 80), PerformanceMode.Smart, MediaKind.Video).TotalThreads;

        Assert.True(quiet > moderate && moderate > heavy, $"{quiet} > {moderate} > {heavy}");
    }

    [Fact]
    public void Maximum_mode_uses_every_core_at_normal_priority()
    {
        var budget = ResourceGovernor.Decide(Machine(busy: 95), PerformanceMode.Maximum, MediaKind.Video);

        Assert.Equal(12, budget.TotalThreads);
        Assert.Equal(ProcessPriorityClass.Normal, budget.Priority);
    }

    [Fact]
    public void Gentle_mode_uses_at_most_half_the_cores_at_low_priority()
    {
        var budget = ResourceGovernor.Decide(Machine(busy: 0), PerformanceMode.Gentle, MediaKind.Image);

        Assert.True(budget.TotalThreads <= 6);
        Assert.True(budget.MaxParallelJobs <= 2);
        Assert.Equal(ProcessPriorityClass.BelowNormal, budget.Priority);
    }

    [Fact]
    public void Low_memory_limits_threads_and_runs_one_job_at_a_time()
    {
        var snapshot = new ResourceSnapshot(12, 0, 1_800_000_000, 8 * GiB);

        var budget = ResourceGovernor.Decide(snapshot, PerformanceMode.Smart, MediaKind.Image);

        Assert.True(budget.TotalThreads <= 2);
        Assert.Equal(1, budget.MaxParallelJobs);
    }

    [Fact]
    public void Videos_run_one_at_a_time_unless_the_processor_is_large_and_idle()
    {
        var small = ResourceGovernor.Decide(Machine(cores: 6), PerformanceMode.Smart, MediaKind.Video);
        var large = ResourceGovernor.Decide(Machine(cores: 16), PerformanceMode.Smart, MediaKind.Video);

        Assert.Equal(1, small.MaxParallelJobs);
        Assert.Equal(2, large.MaxParallelJobs);
        Assert.Equal(7, large.ThreadsPerJob);
    }

    [Fact]
    public void A_two_core_computer_still_gets_one_thread_and_one_job()
    {
        var budget = ResourceGovernor.Decide(Machine(cores: 2, busy: 95), PerformanceMode.Smart, MediaKind.Video);

        Assert.True(budget.TotalThreads >= 1);
        Assert.Equal(1, budget.MaxParallelJobs);
    }

    [Fact]
    public void Nonsense_readings_do_not_break_the_decision()
    {
        var budget = ResourceGovernor.Decide(new ResourceSnapshot(0, 250, -5, 0), PerformanceMode.Smart, MediaKind.Video);

        Assert.True(budget.TotalThreads >= 1);
        Assert.True(budget.MaxParallelJobs >= 1);
    }

    [Fact]
    public void The_real_sampler_returns_plausible_numbers()
    {
        var snapshot = new SystemResourceSampler().Sample();

        Assert.True(snapshot.LogicalCores >= 1);
        Assert.InRange(snapshot.CpuBusyPercent, 0, 100);
        Assert.True(snapshot.AvailableMemoryBytes > 0);
        Assert.True(snapshot.TotalMemoryBytes >= snapshot.AvailableMemoryBytes);
    }

    [Fact]
    public async Task The_scheduler_runs_jobs_in_parallel_but_never_above_the_limit()
    {
        var scheduler = new ConversionScheduler(new FakeSampler(Machine()), PerformanceMode.Smart);
        var items = Enumerable.Range(0, 12).Select(i => (i, MediaKind.Image)).ToList();
        var active = 0;
        var peak = 0;
        var done = 0;

        await scheduler.RunAsync(items, async (_, _, _) =>
        {
            var now = Interlocked.Increment(ref active);
            InterlockedMax(ref peak, now);
            await Task.Delay(60);
            Interlocked.Decrement(ref active);
            Interlocked.Increment(ref done);
        }, CancellationToken.None);

        Assert.Equal(12, done);
        Assert.InRange(peak, 4, 8);
    }

    [Fact]
    public async Task The_scheduler_runs_one_job_at_a_time_when_memory_is_low()
    {
        var snapshot = new ResourceSnapshot(12, 0, 1_500_000_000, 8 * GiB);
        var scheduler = new ConversionScheduler(new FakeSampler(snapshot), PerformanceMode.Smart);
        var items = Enumerable.Range(0, 6).Select(i => (i, MediaKind.Image)).ToList();
        var active = 0;
        var peak = 0;

        await scheduler.RunAsync(items, async (_, _, _) =>
        {
            InterlockedMax(ref peak, Interlocked.Increment(ref active));
            await Task.Delay(30);
            Interlocked.Decrement(ref active);
        }, CancellationToken.None);

        Assert.Equal(1, peak);
    }

    [Fact]
    public async Task Each_job_receives_its_share_of_threads()
    {
        var scheduler = new ConversionScheduler(new FakeSampler(Machine(cores: 16)), PerformanceMode.Smart);
        var budgets = new List<ResourceBudget>();

        await scheduler.RunAsync([(1, MediaKind.Video)], (_, budget, _) =>
        {
            lock (budgets)
            {
                budgets.Add(budget);
            }

            return Task.CompletedTask;
        }, CancellationToken.None);

        var received = Assert.Single(budgets);
        Assert.Equal(14, received.TotalThreads);
        Assert.Equal(7, received.ThreadsPerJob);
    }

    [Fact]
    public async Task Cancelling_stops_new_jobs_from_starting()
    {
        var scheduler = new ConversionScheduler(new FakeSampler(Machine(busy: 95)), PerformanceMode.Smart);
        var items = Enumerable.Range(0, 10).Select(i => (i, MediaKind.Video)).ToList();
        using var cancel = new CancellationTokenSource();
        var started = 0;

        await scheduler.RunAsync(items, async (_, _, token) =>
        {
            if (Interlocked.Increment(ref started) == 2)
            {
                await cancel.CancelAsync();
            }

            await Task.Delay(40, CancellationToken.None);
        }, cancel.Token);

        Assert.InRange(started, 2, 3);
    }

    [Fact]
    public async Task One_failing_job_does_not_stop_the_others_and_is_reported_at_the_end()
    {
        var scheduler = new ConversionScheduler(new FakeSampler(Machine()), PerformanceMode.Smart);
        var items = Enumerable.Range(0, 5).Select(i => (i, MediaKind.Audio)).ToList();
        var completed = 0;

        var failure = await Assert.ThrowsAsync<AggregateException>(() => scheduler.RunAsync(items, (item, _, _) =>
        {
            if (item == 2)
            {
                throw new InvalidOperationException("boom");
            }

            Interlocked.Increment(ref completed);
            return Task.CompletedTask;
        }, CancellationToken.None));

        Assert.Equal(4, completed);
        Assert.Single(failure.InnerExceptions);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref target);
            if (value <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, value, current) != current);
    }
}

public class PhotoBatchSpeedTests
{
    private const long GiB = 1024L * 1024 * 1024;

    private static ResourceSnapshot Machine(int cores, double busy = 0, long freeGiB = 24) =>
        new(cores, busy, freeGiB * GiB, 32 * GiB);

    [Fact]
    public void An_idle_twelve_core_computer_runs_eight_photos_at_once_with_one_thread_each()
    {
        var budget = ResourceGovernor.Decide(Machine(12), PerformanceMode.Smart, MediaKind.Image);

        Assert.Equal(8, budget.MaxParallelJobs);
        Assert.Equal(1, budget.ThreadsPerJob);
    }

    [Theory]
    [InlineData(4, 3)]
    [InlineData(8, 6)]
    [InlineData(2, 1)]
    public void Smaller_computers_run_about_one_photo_per_free_core(int cores, int expectedJobs)
    {
        var budget = ResourceGovernor.Decide(Machine(cores), PerformanceMode.Smart, MediaKind.Image);

        Assert.Equal(expectedJobs, budget.MaxParallelJobs);
    }

    [Fact]
    public void Gentle_mode_still_keeps_photos_to_two_at_a_time()
    {
        var budget = ResourceGovernor.Decide(Machine(12), PerformanceMode.Gentle, MediaKind.Image);

        Assert.True(budget.MaxParallelJobs <= 2);
    }

    [Fact]
    public void Eight_photos_at_once_still_fit_in_memory_limits()
    {
        // 24 GiB free: eight photos at 0.75 GiB each is 6 GiB. With 4 GiB free it must come down.
        var roomy = ResourceGovernor.Decide(Machine(12, freeGiB: 24), PerformanceMode.Smart, MediaKind.Image);
        var tight = ResourceGovernor.Decide(Machine(12, freeGiB: 4), PerformanceMode.Smart, MediaKind.Image);

        Assert.Equal(8, roomy.MaxParallelJobs);
        Assert.InRange(tight.MaxParallelJobs, 1, 3);
    }

    [Fact]
    public void The_webp_encoder_is_never_asked_for_its_slow_lossless_mode()
    {
        var plan = CommandBuilder.Build(
            new ConversionRequest(@"C:\p\a.jpg", TargetFormat.WebP, Quality: 100),
            @"C:\Out\a.webp",
            @"C:\Temp");

        var arguments = plan.Steps[0].Arguments.ToList();
        Assert.Equal("99", arguments[arguments.IndexOf("-quality") + 1]);
    }

    [Fact]
    public void Other_formats_keep_the_quality_the_slider_says()
    {
        var plan = CommandBuilder.Build(
            new ConversionRequest(@"C:\p\a.png", TargetFormat.Jpeg, Quality: 100),
            @"C:\Out\a.jpg",
            @"C:\Temp");

        var arguments = plan.Steps[0].Arguments.ToList();
        Assert.Equal("100", arguments[arguments.IndexOf("-quality") + 1]);
    }
}
