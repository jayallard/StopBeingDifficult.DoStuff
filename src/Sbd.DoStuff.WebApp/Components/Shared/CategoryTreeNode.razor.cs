using Microsoft.AspNetCore.Components;
using Sbd.DoStuff.Domain.Execution;
using Sbd.DoStuff.Domain.Icons;
using Sbd.DoStuff.Domain.Lists;
using Sbd.DoStuff.WebApp.Editing;

namespace Sbd.DoStuff.WebApp.Components.Shared;

public partial class CategoryTreeNode : IDisposable
{
    [Parameter, EditorRequired] public CategoryNode Node { get; set; } = null!;
    [Parameter, EditorRequired] public string ListId { get; set; } = "";

    /// <summary>Raised after an edited entry was saved, so the page can reload the list and rebuild the tree.</summary>
    [Parameter] public EventCallback OnSaved { get; set; }

    private TaskListEntryView? _editingView;
    private EntryEditModel? _editing;
    private string? _editError;

    private readonly Dictionary<string, TaskRun> _lastRuns = new();
    private readonly Dictionary<string, string> _runErrors = new();

    protected override void OnInitialized()
    {
        foreach (var task in Node.Tasks)
        {
            // A list can hold the same definition several times with different parameter values, so the
            // last run is tracked per entry, not per definition id. Runs made before entries were keyed
            // have no EntryKey and are ignored.
            var key = KeyOf(task);
            var lastRun = RunStore.GetRecentForTask(ListId, task.Definition.Id, int.MaxValue)
                .FirstOrDefault(r => r.EntryKey == key);
            if (lastRun is not null)
            {
                _lastRuns[key] = lastRun;
            }
        }

        Engine.RunChanged += OnRunChanged;
    }

    private void OnRunChanged(TaskRun run)
    {
        if (run.ListId == ListId && run.EntryKey is not null && Node.Tasks.Any(t => KeyOf(t) == run.EntryKey))
        {
            _lastRuns[run.EntryKey] = run;
            InvokeAsync(StateHasChanged);
        }
    }

    private void Run(TaskListEntryView view)
    {
        _runErrors.Remove(view.Definition.Id);

        if (view.IsDisabled)
        {
            return;
        }

        try
        {
            var task = TaskFactory.Create(view.Definition, view.ParameterValues);
            var run = Engine.StartRun(task, ListId, KeyOf(view));
            Navigation.NavigateTo($"lists/{ListId}/tasks/{view.Definition.Id}/runs/{run.RunId}");
        }
        catch (Exception ex)
        {
            _runErrors[view.Definition.Id] = ex.Message;
        }
    }

    // Clears the run history of every task in this category and its descendants, then asks the page to rebuild the tree.
    private async Task ResetCategory()
    {
        var keys = new HashSet<string>();
        CollectKeys(Node, keys);
        RunStore.ClearForEntries(ListId, keys);
        await OnSaved.InvokeAsync();
    }

    private static void CollectKeys(CategoryNode node, HashSet<string> keys)
    {
        foreach (var task in node.Tasks)
        {
            keys.Add(KeyOf(task));
        }

        foreach (var child in node.Children.Values)
        {
            CollectKeys(child, keys);
        }
    }

    private void StartEdit(TaskListEntryView view)
    {
        var source = view.Source!;
        _editError = null;
        _editingView = view;
        _editing = new EntryEditModel(source.TaskId, source.Categories, source.ParameterValues, source.Notes, source.Name, source.DisabledReason) { Icon = source.Icon };
    }

    private void CancelEdit()
    {
        _editingView = null;
        _editing = null;
        _editError = null;
    }

    private async Task SaveEdit()
    {
        if (_editingView?.Source is not { } source || _editing is null)
        {
            return;
        }

        var list = ListRepository.Find(ListId);
        var index = list is null ? -1 : IndexOfEntry(list, source);
        if (list is null || index < 0)
        {
            _editError = "This list changed since it was loaded; reload the page and try again.";
            return;
        }

        var updated = new TaskListEntry(
            source.TaskId,
            source.Categories,
            _editing.Values.Count == 0 ? null : new Dictionary<string, string>(_editing.Values),
            string.IsNullOrWhiteSpace(_editing.Notes) ? null : _editing.Notes.Trim(),
            string.IsNullOrWhiteSpace(_editing.Name) ? null : _editing.Name.Trim(),
            _editing.PersistedDisabledReason,
            string.IsNullOrWhiteSpace(_editing.Icon) ? null : _editing.Icon.Trim());

        var entries = list.Entries.ToList();
        entries[index] = updated;
        var definition = list with { Entries = entries };

        var errors = TaskListValidator.Validate(definition, Library);
        if (errors.Count > 0)
        {
            _editError = string.Join(" ", errors);
            return;
        }

        try
        {
            ListRepository.Save(definition, ListRepository.GetScope(ListId) ?? TaskListScope.Private, ListId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            _editError = ex.Message;
            return;
        }

        CancelEdit();
        await OnSaved.InvokeAsync();
    }

    private static int IndexOfEntry(TaskListDefinition list, TaskListEntry source)
    {
        for (var i = 0; i < list.Entries.Count; i++)
        {
            if (ReferenceEquals(list.Entries[i], source))
            {
                return i;
            }
        }

        return -1;
    }

    private string IconUrl(TaskListEntryView view) => $"icons/{Icons.Resolve(view.Icon)}";

    private static string KeyOf(TaskListEntryView view) => TaskRunKey.For(view.Definition.Id, view.ParameterValues);

    private string RowClass(TaskListEntryView view)
    {
        const string baseClass = "flex items-center justify-between rounded border px-3 py-2 transition";

        if (view.IsDisabled)
        {
            return $"{baseClass} border-slate-200 bg-slate-50 opacity-60";
        }

        if (!_lastRuns.TryGetValue(KeyOf(view), out var lastRun) || lastRun.Status != TaskRunStatus.Succeeded && lastRun.Status != TaskRunStatus.Failed)
        {
            return $"{baseClass} border-slate-200 bg-white hover:border-indigo-300";
        }

        return lastRun.ResultCode == 0
            ? $"{baseClass} border-emerald-300 bg-emerald-50 hover:border-emerald-400"
            : $"{baseClass} border-red-300 bg-red-50 hover:border-red-400";
    }

    public void Dispose() => Engine.RunChanged -= OnRunChanged;
}
