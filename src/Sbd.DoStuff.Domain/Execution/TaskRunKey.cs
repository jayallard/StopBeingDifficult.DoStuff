using System.Security.Cryptography;
using System.Text;

namespace Sbd.DoStuff.Domain.Execution;

/// <summary>
/// Identifies one Task List entry across runs. A list may reference the same Task Definition several
/// times with different parameter values, so (list, task id) alone can't tell those entries apart.
/// </summary>
public static class TaskRunKey
{
    public static string For(string taskId, IReadOnlyDictionary<string, string> parameterValues)
    {
        var text = new StringBuilder(taskId);
        foreach (var (name, value) in parameterValues.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            text.Append('\n').Append(name).Append('=').Append(value);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }
}
