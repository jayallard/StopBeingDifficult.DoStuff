using Sbd.DoStuff.Domain.Library;

namespace Sbd.DoStuff.Domain.Lists;

public sealed record TaskListEntryView(
    EffectiveTaskDefinition Definition,
    IReadOnlyDictionary<string, string> ParameterValues);
