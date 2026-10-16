using System.IO.Pipes;
using System.Text.Json;
using Mercury.Converter.Core;

namespace Mercury.Converter.App.Services;

/// <summary>
/// Explorer starts the program once for every selected file. This makes the first one the owner: the others pass their
/// file to it and quit, so selecting ten photos opens one window with ten photos, not ten windows.
/// </summary>
public sealed class StartupCoordinator : IDisposable
{
    private static readonly string Suffix = Environment.UserName.Replace('\\', '_');
    private static readonly string MutexName = $@"Local\MercuryConverter.Startup.{Suffix}";
    private static readonly string PipeName = $"MercuryConverter.Startup.{Suffix}";

    private readonly Mutex mutex;
    private readonly CancellationTokenSource stop = new();
    private readonly List<ConvertCommand> collected = [];
    private readonly object gate = new();
    private Action<ConvertCommand>? handler;

    private StartupCoordinator(Mutex mutex) => this.mutex = mutex;

    /// <summary>
    /// Returns the owner when this is the first process. Returns null when another one is running: the command has
    /// then been handed over and this process should exit.
    /// </summary>
    public static StartupCoordinator? TryBecomeOwner(ConvertCommand command)
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var isNew);
        if (isNew)
        {
            var owner = new StartupCoordinator(mutex);
            owner.Listen();
            return owner;
        }

        mutex.Dispose();
        return Send(command) ? null : new StartupCoordinator(new Mutex(false));
    }

    private static bool Send(ConvertCommand command)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(3000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(JsonSerializer.Serialize(command));
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            // The owner went away. This process carries on by itself.
            return false;
        }
    }

    private void Listen()
    {
        _ = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(stop.Token);
                    using var reader = new StreamReader(server);
                    var line = await reader.ReadLineAsync(stop.Token);
                    if (line is not null && JsonSerializer.Deserialize<ConvertCommand>(line) is { } command)
                    {
                        Deliver(command);
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex) when (ex is IOException or JsonException)
                {
                    // A broken message is ignored. The next one is read normally.
                }
            }
        });
    }

    private void Deliver(ConvertCommand command)
    {
        lock (gate)
        {
            if (handler is null)
            {
                collected.Add(command);
                return;
            }
        }

        handler(command);
    }

    /// <summary>
    /// Waits briefly for the other files of a multi-file selection, then returns them all as one command.
    /// </summary>
    public async Task<ConvertCommand> GatherAsync(ConvertCommand first, TimeSpan wait)
    {
        await Task.Delay(wait);
        lock (gate)
        {
            var files = first.Files.Concat(collected.SelectMany(c => c.Files)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            collected.Clear();
            return first with { Files = files };
        }
    }

    /// <summary>From now on, files that arrive are passed to <paramref name="onCommand"/> (on any thread).</summary>
    public void Attach(Action<ConvertCommand> onCommand)
    {
        List<ConvertCommand> late;
        lock (gate)
        {
            handler = onCommand;
            late = [.. collected];
            collected.Clear();
        }

        foreach (var command in late)
        {
            onCommand(command);
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        stop.Dispose();
        mutex.Dispose();
    }
}
