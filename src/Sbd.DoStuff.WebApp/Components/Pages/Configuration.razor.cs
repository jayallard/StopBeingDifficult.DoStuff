using Microsoft.JSInterop;
using Sbd.DoStuff.Domain.DataSources;

namespace Sbd.DoStuff.WebApp.Components.Pages;

public partial class Configuration
{
    private const int MaxListedIds = 8;

    private bool _confirmRestart;
    private bool _restarting;

    private async Task RestartAsync()
    {
        _restarting = true;
        await JS.InvokeVoidAsync("reloadWhenServerRestarts");
        Restarter.Restart();
    }

    public static string SourceAnchor(int sourceIndex) => $"source-{sourceIndex}";

    public static string KindAnchor(int sourceIndex, DataItemKind kind) => $"source-{sourceIndex}-{kind.ToString().ToLowerInvariant()}";

    public static string KindTitle(DataItemKind kind) => kind switch
    {
        DataItemKind.TaskLibrary => "Task library files",
        DataItemKind.TaskList => "Task lists",
        DataItemKind.Asset => "Assets",
        _ => kind.ToString(),
    };

    private static string ProvidesSummary(DataItem item)
    {
        if (item.Kind != DataItemKind.TaskLibrary)
        {
            return string.Join(", ", item.Provides);
        }

        var noun = item.Provides.Count == 1 ? "definition" : "definitions";
        var shown = string.Join(", ", item.Provides.Take(MaxListedIds));
        return $"{item.Provides.Count} {noun}: {shown}{(item.Provides.Count > MaxListedIds ? ", ..." : "")}";
    }

    private static string FormatSize(long? bytes) => bytes switch
    {
        null => "",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB",
    };
}
