using System.Text.RegularExpressions;

namespace Sbd.DoStuff.Domain.Library;

/// <summary>
/// Tokens that don't match a declared parameter name are left as literal text — a known
/// v1 simplification; authoring mismatches show up immediately when the task actually runs.
/// </summary>
public static partial class ParameterTemplate
{
    public static string Substitute(string template, IReadOnlyDictionary<string, string> values) =>
        TokenPattern().Replace(template, match =>
            values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex TokenPattern();
}
