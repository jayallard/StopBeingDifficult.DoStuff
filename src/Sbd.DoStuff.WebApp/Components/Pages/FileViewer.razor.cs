using Microsoft.AspNetCore.Components;
using Sbd.DoStuff.Domain.DataSources;
using Sbd.DoStuff.WebApp.Syntax;

namespace Sbd.DoStuff.WebApp.Components.Pages;

public partial class FileViewer
{
    private DataItem? _item;
    private string? _text;
    private string? _language;
    private string? _html;
    private int _lineCount;

    [SupplyParameterFromQuery(Name = "path")] public string? Path { get; set; }

    protected override void OnParametersSet()
    {
        _item = null;
        _text = null;

        if (string.IsNullOrEmpty(Path))
        {
            return;
        }

        // Only items a data source itself lists can be shown, so this can't be used to read arbitrary files.
        foreach (var source in DataSources)
        {
            var item = source.GetItems().FirstOrDefault(i => i.Location == Path);
            var text = item is null ? null : source.ReadText(item);
            if (item is not null && text is not null)
            {
                _item = item;
                _text = text;
                break;
            }
        }

        if (_item is null || _text is null)
        {
            return;
        }

        _language = SyntaxHighlighter.LanguageFor(_item.Name);
        _html = SyntaxHighlighter.Highlight(_text, _language);
        _lineCount = _text.Length == 0 ? 0 : _text.Split('\n').Length;
    }

    private static string FormatSize(long? bytes) => bytes switch
    {
        null => "",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB",
    };
}
