using Microsoft.AspNetCore.Components;
using Sbd.DoStuff.Domain.Library;

namespace Sbd.DoStuff.WebApp.Components.Shared;

public partial class ParameterValueText
{
    [Parameter] public TaskParameterType Type { get; set; }
    [Parameter] public string? Value { get; set; }
}
