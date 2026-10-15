using Microsoft.AspNetCore.Components;
using Sbd.DoStuff.Domain.Lists;

namespace Sbd.DoStuff.WebApp.Components.Shared;

public partial class OutlineNode
{
    [Parameter, EditorRequired] public CategoryNode Node { get; set; } = null!;
    [Parameter, EditorRequired] public string ListId { get; set; } = "";
    [Parameter] public int Depth { get; set; }
}
