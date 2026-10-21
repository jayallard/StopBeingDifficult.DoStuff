using Sbd.DoStuff.Domain.Execution;
using Sbd.DoStuff.Domain.Lists;

namespace Sbd.DoStuff.WebApp.Components.Shared;

public partial class RunningTasks
{
    private sealed record RunningItem(TaskRun Run, string Name, string IconUrl);

    private IReadOnlyList<RunningItem> _running = [];
    private bool _open;
    private Timer? _timer;

    protected override void OnInitialized()
    {
        Refresh();
        Engine.RunChanged += OnRunChanged;

        // Re-render each second so the elapsed times tick while the dropdown is open.
        _timer = new Timer(_ =>
        {
            if (_open && _running.Count > 0)
            {
                InvokeAsync(StateHasChanged);
            }
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private static string Elapsed(TaskRun run)
    {
        var elapsed = DateTimeOffset.Now - run.StartedAt;
        return elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"m\:ss");
    }

    private void Refresh()
    {
        var runs = RunStore.GetAll()
            .Where(r => r.Status is TaskRunStatus.Pending or TaskRunStatus.Running)
            .OrderBy(r => r.StartedAt)
            .ToList();

        // Entries are looked up by (list, entry key), so two runs of the same definition show their own entry names.
        var entriesByList = new Dictionary<string, Dictionary<string, TaskListEntryView>>();
        _running = runs.Select(run =>
        {
            TaskListEntryView? view = null;
            if (run.EntryKey is not null)
            {
                if (!entriesByList.TryGetValue(run.ListId, out var entries))
                {
                    entries = EntriesOf(run.ListId);
                    entriesByList[run.ListId] = entries;
                }

                entries.TryGetValue(run.EntryKey, out view);
            }

            return new RunningItem(run, view?.Name ?? view?.Definition.Name ?? run.TaskName, $"icons/{Icons.Resolve(view?.Icon)}");
        }).ToList();
    }

    private Dictionary<string, TaskListEntryView> EntriesOf(string listId)
    {
        var result = new Dictionary<string, TaskListEntryView>();
        if (ListRepository.Find(listId) is { } list)
        {
            Collect(CategoryTreeBuilder.Build(list.Entries, Library), result);
        }

        return result;
    }

    private static void Collect(CategoryNode node, Dictionary<string, TaskListEntryView> result)
    {
        foreach (var task in node.Tasks)
        {
            result.TryAdd(TaskRunKey.For(task.Definition.Id, task.ParameterValues), task);
        }

        foreach (var child in node.Children.Values)
        {
            Collect(child, result);
        }
    }

    // RunChanged fires from the background execution thread.
    private void OnRunChanged(TaskRun run) => InvokeAsync(() =>
    {
        Refresh();
        StateHasChanged();
    });

    private void Toggle() => _open = !_open;

    private void Close() => _open = false;

    public void Dispose()
    {
        Engine.RunChanged -= OnRunChanged;
        _timer?.Dispose();
    }
}
