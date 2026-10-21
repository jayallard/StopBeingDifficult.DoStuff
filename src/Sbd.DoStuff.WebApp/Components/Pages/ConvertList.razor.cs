using Microsoft.AspNetCore.Components;
using Sbd.DoStuff.Domain.Library;
using Sbd.DoStuff.Domain.Lists;

namespace Sbd.DoStuff.WebApp.Components.Pages;

public partial class ConvertList
{
    private string _listId = "";
    private string _libraryId = "";
    private IReadOnlyList<EntryConversion>? _plan;
    private readonly HashSet<int> _selected = [];
    private IReadOnlyList<TaskLibraryGroup> _eligible = [];
    private string? _message;
    private string? _error;

    private void OnListChanged(ChangeEventArgs e)
    {
        _listId = (string?)e.Value ?? "";
        _message = null;
        _eligible = ListRepository.Find(_listId) is null ? [] : Converter.EligibleLibraries(_listId);
        if (!_eligible.Any(g => g.Id == _libraryId))
        {
            _libraryId = "";
        }

        Refresh();
    }

    private void OnLibraryChanged(ChangeEventArgs e)
    {
        _libraryId = (string?)e.Value ?? "";
        _message = null;
        Refresh();
    }

    private void Refresh()
    {
        _error = null;
        _selected.Clear();
        _plan = null;

        var list = ListRepository.Find(_listId);
        if (list is null || _libraryId.Length == 0)
        {
            return;
        }

        _plan = Converter.Plan(list, _libraryId);
    }

    private void Toggle(int index, bool selected)
    {
        if (selected)
        {
            _selected.Add(index);
        }
        else
        {
            _selected.Remove(index);
        }
    }

    private void ConvertSelected()
    {
        try
        {
            var count = Converter.Convert(_listId, _libraryId, _selected.ToList());
            _message = $"Converted {count} {(count == 1 ? "task" : "tasks")}.";
            Refresh();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    private static string DisplayName(EntryConversion row) =>
        string.IsNullOrWhiteSpace(row.Entry.Name) ? row.Base?.Name ?? row.Entry.TaskId : row.Entry.Name!;

    private static string? StatusNote(EntryConversion row) => row.Status switch
    {
        EntryConversionStatus.InTargetLibrary => "already in this library",
        EntryConversionStatus.PlainReference => "plain reference, nothing to convert",
        EntryConversionStatus.OutsideSharedFolders => "from another data folder; can't be shared into this library",
        EntryConversionStatus.UnknownTask => "unknown task",
        _ => null,
    };
}
