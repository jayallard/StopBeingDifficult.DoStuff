using Microsoft.AspNetCore.Components;
using Sbd.DoStuff.Domain.Lists;

namespace Sbd.DoStuff.WebApp.Components.Pages;

public partial class ListDetail
{
    [Parameter] public string ListId { get; set; } = "";

    private TaskListDefinition? _list;
    private CategoryNode? _root;
    private int _resetVersion;

    protected override void OnParametersSet()
    {
        _list = ListRepository.Find(ListId);
        _root = _list is null ? null : CategoryTreeBuilder.Build(_list.Entries, Library);
    }

    private void Reset()
    {
        if (_list is null)
        {
            return;
        }

        RunStore.ClearForList(ListId);
        _resetVersion++;
    }
}
