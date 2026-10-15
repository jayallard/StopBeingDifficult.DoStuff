using System.Text.Json;
using Sbd.DoStuff.Domain.Execution;

namespace Sbd.DoStuff.WebApp.Services;

/// <summary>A command a task asked of the user by writing a <c>::Name</c> output line.</summary>
public sealed record PendingCommand(string Name, int Count, DateTimeOffset FirstRaisedAt, DateTimeOffset LastRaisedAt);

/// <summary>
/// Collects <c>::</c> command lines from task output, de-duplicating by command (with an occurrence count).
/// Pending commands are persisted so they survive an app restart (a new process, so in-memory state is lost);
/// a command is dropped on load once what it asks for has happened since it was last raised.
/// </summary>
public sealed class PendingCommands : IHostedService, IDisposable
{
    public const string CommandPrefix = "::";
    public const string RestartApplication = "RestartApplication";
    public const string RestartComputer = "RestartComputer";

    private static readonly string[] KnownCommands = [RestartApplication, RestartComputer];

    private readonly ITaskExecutionEngine _engine;
    private readonly string _filePath;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, int> _scanned = [];
    private readonly List<PendingCommand> _pending = [];

    public PendingCommands(ITaskExecutionEngine engine, IConfiguration configuration)
        : this(engine, ResolveFilePath(configuration), DateTimeOffset.UtcNow, BootTime())
    {
    }

    internal PendingCommands(ITaskExecutionEngine engine, string filePath, DateTimeOffset processStart, DateTimeOffset bootTime)
    {
        _engine = engine;
        _filePath = filePath;
        Load(processStart, bootTime);
    }

    public event Action? Changed;

    public IReadOnlyList<PendingCommand> Pending
    {
        get
        {
            lock (_gate)
            {
                return _pending.ToArray();
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _engine.RunChanged += OnRunChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _engine.RunChanged -= OnRunChanged;
        return Task.CompletedTask;
    }

    public void Dispose() => _engine.RunChanged -= OnRunChanged;

    public static bool IsCommandLine(string text) => text.StartsWith(CommandPrefix, StringComparison.Ordinal);

    public static string CommandName(string text) => text[CommandPrefix.Length..].Trim();

    public void Dismiss(string name)
    {
        lock (_gate)
        {
            _pending.RemoveAll(c => c.Name == name);
            Save();
        }

        Changed?.Invoke();
    }

    internal void OnRunChanged(TaskRun run)
    {
        var raised = false;

        lock (_gate)
        {
            var lines = run.OutputLines;
            var start = _scanned.GetValueOrDefault(run.RunId);

            for (var i = start; i < lines.Count; i++)
            {
                if (IsCommandLine(lines[i].Text) && Raise(CommandName(lines[i].Text), lines[i].Timestamp))
                {
                    raised = true;
                }
            }

            if (run.CompletedAt is not null)
            {
                _scanned.Remove(run.RunId);
            }
            else
            {
                _scanned[run.RunId] = lines.Count;
            }

            if (raised)
            {
                Save();
            }
        }

        if (raised)
        {
            Changed?.Invoke();
        }
    }

    private bool Raise(string name, DateTimeOffset at)
    {
        var known = KnownCommands.FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (known is null)
        {
            return false;
        }

        var index = _pending.FindIndex(c => c.Name == known);
        if (index < 0)
        {
            _pending.Add(new PendingCommand(known, 1, at, at));
        }
        else
        {
            _pending[index] = _pending[index] with { Count = _pending[index].Count + 1, LastRaisedAt = at };
        }

        return true;
    }

    private void Load(DateTimeOffset processStart, DateTimeOffset bootTime)
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            var saved = JsonSerializer.Deserialize<List<PendingCommand>>(File.ReadAllText(_filePath)) ?? [];
            foreach (var command in saved)
            {
                var since = command.Name == RestartComputer ? bootTime : processStart;
                if (command.LastRaisedAt >= since)
                {
                    _pending.Add(command);
                }
            }

            if (_pending.Count != saved.Count)
            {
                Save();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged or unreadable file just means starting with nothing pending.
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_pending));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Persistence is best effort; the in-memory list still drives the banner.
        }
    }

    private static string ResolveFilePath(IConfiguration configuration)
    {
        var path = configuration["PendingCommands:File"] ?? "~/.sbd/PendingCommands.json";
        return path.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..])
            : path;
    }

    private static DateTimeOffset BootTime() =>
        DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
}
