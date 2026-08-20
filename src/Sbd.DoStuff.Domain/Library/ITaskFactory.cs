using Sbd.DoStuff.Domain.Tasks;

namespace Sbd.DoStuff.Domain.Library;

public interface ITaskFactory
{
    ITask Create(EffectiveTaskDefinition effective, IReadOnlyDictionary<string, string> allParameterValues);
}
