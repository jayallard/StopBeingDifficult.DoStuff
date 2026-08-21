using Sbd.DoStuff.Domain.Library;
using Sbd.DoStuff.Domain.Lists;
using Sbd.DoStuff.Domain.Validation;
using Sbd.DoStuff.UnitTests.Fakes;
using Shouldly;

namespace Sbd.DoStuff.UnitTests.Validation;

public class TaskListValidationTests
{
    private static TaskDefinition ShellTask(string id, params TaskParameterDefinition[] parameters) =>
        new(id, id, null, null, null, "powershell", $"echo {id}", null, null, parameters);

    [Fact]
    public async Task ValidLibraryAndLists_DoesNotThrow()
    {
        var library = new FakeTaskLibrary(ShellTask("task-a"));
        var list = new TaskListDefinition("list-a", "List A", null, [new TaskListEntry("task-a", ["a"], null)]);
        var service = new TaskListValidationHostedService(new FakeTaskListRepository(list), library);

        await Should.NotThrowAsync(() => service.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ListReferencingUnknownTaskId_Throws()
    {
        var library = new FakeTaskLibrary();
        var list = new TaskListDefinition("list-a", "List A", null, [new TaskListEntry("missing-task", ["a"], null)]);
        var service = new TaskListValidationHostedService(new FakeTaskListRepository(list), library);

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => service.StartAsync(CancellationToken.None));
        ex.Message.ShouldContain("missing-task");
    }

    [Fact]
    public async Task ListOmittingRequiredParameter_Throws()
    {
        var library = new FakeTaskLibrary(ShellTask("delete-folder", new TaskParameterDefinition("FolderName", null, true, null)));
        var list = new TaskListDefinition("list-a", "List A", null, [new TaskListEntry("delete-folder", ["a"], null)]);
        var service = new TaskListValidationHostedService(new FakeTaskListRepository(list), library);

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => service.StartAsync(CancellationToken.None));
        ex.Message.ShouldContain("FolderName");
    }

    [Fact]
    public async Task LibraryWithBaseChainCycle_Throws()
    {
        var a = new TaskDefinition("a", "A", null, "b", new Dictionary<string, string>(), null, null, null, null, null);
        var b = new TaskDefinition("b", "B", null, "a", new Dictionary<string, string>(), null, null, null, null, null);
        var library = new FakeTaskLibrary(a, b);
        var service = new TaskListValidationHostedService(new FakeTaskListRepository(), library);

        await Should.ThrowAsync<InvalidOperationException>(() => service.StartAsync(CancellationToken.None));
    }
}
