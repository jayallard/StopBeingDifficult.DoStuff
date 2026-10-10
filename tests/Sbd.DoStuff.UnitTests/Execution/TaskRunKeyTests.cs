using Sbd.DoStuff.Domain.Execution;
using Shouldly;

namespace Sbd.DoStuff.UnitTests.Execution;

public class TaskRunKeyTests
{
    [Fact]
    public void SameTaskAndValues_GiveSameKey_RegardlessOfOrder()
    {
        var a = TaskRunKey.For("t", new Dictionary<string, string> { ["A"] = "1", ["B"] = "2" });
        var b = TaskRunKey.For("t", new Dictionary<string, string> { ["B"] = "2", ["A"] = "1" });

        a.ShouldBe(b);
    }

    [Fact]
    public void DifferentValues_GiveDifferentKeys()
    {
        var a = TaskRunKey.For("execute-sql-file", new Dictionary<string, string> { ["ScriptPath"] = "one.sql" });
        var b = TaskRunKey.For("execute-sql-file", new Dictionary<string, string> { ["ScriptPath"] = "two.sql" });

        a.ShouldNotBe(b);
    }

    [Fact]
    public void RunStartedWithKey_KeepsIt()
    {
        var engine = new TaskExecutionEngine(new Fakes.FakeProcessRunner(), new InMemoryTaskRunStore());

        var run = engine.StartRun(new Fakes.FakeTask(run: (_, _) => Task.CompletedTask), "list-a", "key-1");

        run.EntryKey.ShouldBe("key-1");
    }
}
