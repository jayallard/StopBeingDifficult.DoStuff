using Microsoft.AspNetCore.Components;
using Sbd.DoStuff.Domain.Icons;

namespace Sbd.DoStuff.WebApp.Components.Shared;

public partial class IconUsageList
{
    [Parameter, EditorRequired] public IconReference Reference { get; set; } = null!;
}
