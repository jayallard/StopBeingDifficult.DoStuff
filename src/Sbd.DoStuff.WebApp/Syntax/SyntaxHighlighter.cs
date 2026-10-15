using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Sbd.DoStuff.WebApp.Syntax;

/// <summary>
/// A small regex-based highlighter for the file types the app's data uses. It returns HTML
/// (all text escaped) whose spans carry <c>tok-*</c> classes defined in Styles/app.css.
/// </summary>
public static class SyntaxHighlighter
{
    private const string Yaml = "YAML";
    private const string PowerShell = "PowerShell";
    private const string Sql = "SQL";
    private const string Json = "JSON";

    private const string PsKeywords =
        "begin|break|catch|class|continue|data|do|dynamicparam|else|elseif|end|exit|filter|finally|for|foreach|" +
        "from|function|if|in|param|process|return|switch|throw|trap|try|until|using|while|workflow";

    private const string SqlKeywords =
        "add|all|alter|and|as|asc|backup|begin|between|by|case|catch|check|column|constraint|create|cursor|" +
        "database|declare|default|delete|desc|distinct|drop|else|end|exec|execute|exists|foreign|from|full|" +
        "go|grant|group|having|if|in|index|inner|insert|into|is|join|key|left|like|login|not|null|on|or|" +
        "order|outer|primary|print|procedure|references|restore|revoke|right|role|select|set|table|then|" +
        "throw|top|try|union|unique|update|use|user|values|view|when|where|while|with";

    private static readonly Dictionary<string, Regex> Grammars = new(StringComparer.Ordinal)
    {
        [Yaml] = Build(RegexOptions.Multiline,
            ("comment", @"(?<!\S)#[^\r\n]*"),
            ("string", @"""(?:\\.|[^""\\\r\n])*""|'(?:''|[^'\r\n])*'"),
            ("key", @"(?<=^[ \t]*(?:-[ \t]+)*)[^\s#:'""\-][^:#\r\n]*?(?=:(?:[ \t]|\r?$))"),
            ("keyword", @"\b(?:true|false|null)\b"),
            ("number", @"(?<![\w.\-])-?\d+(?:\.\d+)?(?![\w.\-])")),

        [PowerShell] = Build(RegexOptions.IgnoreCase,
            ("comment", @"<#[\s\S]*?#>|#[^\r\n]*"),
            ("string", @"@""[\s\S]*?""@|@'[\s\S]*?'@|""(?:`.|""""|[^""`])*""|'(?:''|[^'])*'"),
            ("variable", @"\$(?:\{[^}\r\n]*\}|(?:\w+:)?\w+|[?_$^])"),
            ("keyword", @"(?<![\w\-$])(?:" + PsKeywords + @")(?![\w\-])"),
            ("function", @"\b[A-Za-z]+-[A-Za-z][A-Za-z0-9]*\b"),
            ("param", @"(?<![\w\-])-[A-Za-z]\w*"),
            ("number", @"\b\d+(?:\.\d+)?\b")),

        [Sql] = Build(RegexOptions.IgnoreCase,
            ("comment", @"--[^\r\n]*|/\*[\s\S]*?\*/"),
            ("string", @"N?'(?:''|[^'])*'"),
            ("variable", @"@@?\w+|\$\(\w+\)"),
            ("key", @"\[[^\]\r\n]*\]"),
            ("keyword", @"\b(?:" + SqlKeywords + @")\b"),
            ("number", @"\b\d+(?:\.\d+)?\b")),

        [Json] = Build(RegexOptions.None,
            ("key", @"""(?:\\.|[^""\\])*""(?=\s*:)"),
            ("string", @"""(?:\\.|[^""\\])*"""),
            ("keyword", @"\b(?:true|false|null)\b"),
            ("number", @"-?\d+(?:\.\d+)?(?:[eE][+\-]?\d+)?")),
    };

    private static readonly string[] TokenClasses = ["comment", "string", "key", "keyword", "variable", "function", "param", "number"];

    /// <summary>The language shown for a file name, or null when it isn't one we highlight.</summary>
    public static string? LanguageFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".yaml" or ".yml" => Yaml,
        ".ps1" or ".psm1" or ".psd1" => PowerShell,
        ".sql" => Sql,
        ".json" => Json,
        _ => null,
    };

    /// <summary>HTML-escaped <paramref name="text"/> with token spans for <paramref name="language"/> (plain if unknown).</summary>
    public static string Highlight(string text, string? language)
    {
        if (language is null || !Grammars.TryGetValue(language, out var grammar))
        {
            return WebUtility.HtmlEncode(text);
        }

        var html = new StringBuilder(text.Length + text.Length / 4);
        var position = 0;

        foreach (Match match in grammar.Matches(text))
        {
            if (match.Length == 0)
            {
                continue;
            }

            html.Append(WebUtility.HtmlEncode(text[position..match.Index]));
            var tokenClass = TokenClasses.First(c => match.Groups[c].Success);
            html.Append("<span class=\"tok-").Append(tokenClass).Append("\">")
                .Append(WebUtility.HtmlEncode(match.Value)).Append("</span>");
            position = match.Index + match.Length;
        }

        html.Append(WebUtility.HtmlEncode(text[position..]));
        return html.ToString();
    }

    // Each rule becomes a named group; the first alternative matching at the leftmost position wins.
    private static Regex Build(RegexOptions options, params (string Name, string Pattern)[] rules) =>
        new(string.Join("|", rules.Select(r => $"(?<{r.Name}>{r.Pattern})")),
            options | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
}
