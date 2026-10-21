using Microsoft.AspNetCore.Components.Web;
using Sbd.DoStuff.Domain.Lists;

namespace Sbd.DoStuff.WebApp.Components.Pages;

public partial class Lists
{
    private string? _renamingId;
    private string _renameValue = "";
    private string? _confirmDeleteId;
    private string? _error;

    private void StartRename(TaskListDefinition list)
    {
        _confirmDeleteId = null;
        _renamingId = list.Id;
        _renameValue = list.Name;
        _error = null;
    }

    private void CancelRename()
    {
        _renamingId = null;
        _error = null;
    }

    private void OnRenameKey(KeyboardEventArgs e, TaskListDefinition list)
    {
        if (e.Key == "Enter")
        {
            Rename(list);
        }
        else if (e.Key == "Escape")
        {
            CancelRename();
        }
    }

    private void Rename(TaskListDefinition list)
    {
        var name = _renameValue.Trim();
        if (name.Length == 0)
        {
            _error = "Name is required.";
            return;
        }

        try
        {
            var directory = ListRepository.GetStorageDirectory(list.Id) ?? ListRepository.StorageDirectories[0];
            ListRepository.Save(list with { Name = name }, directory, list.Id);
            CancelRename();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    private void StartDelete(TaskListDefinition list)
    {
        CancelRename();
        _confirmDeleteId = list.Id;
    }

    private void Delete(TaskListDefinition list)
    {
        ListRepository.Delete(list.Id);
        _confirmDeleteId = null;
    }
}
