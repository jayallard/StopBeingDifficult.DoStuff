using Sbd.DoStuff.Domain.Library;
using Sbd.DoStuff.Domain.Lists;
using Sbd.DoStuff.UnitTests.Fakes;
using Shouldly;

namespace Sbd.DoStuff.UnitTests.Lists;

public class CategoryTreeBuilderTests
{
    private static TaskDefinition ShellTask(string id, params TaskParameterDefinition[] parameters) =>
        new(id, id, null, null, null, "shell", $"echo {id}", null, null, parameters);

    [Fact]
    public void NestedPath_CreatesRightNodeChain()
    {
        var library = new FakeTaskLibrary(ShellTask("task-a"));
        var entries = new[] { new TaskListEntry("task-a", ["a.b.c"], null) };

        var root = CategoryTreeBuilder.Build(entries, library);

        var leaf = root.Children["a"].Children["b"].Children["c"];
        leaf.Tasks.ShouldHaveSingleItem();
        leaf.Tasks[0].Definition.Id.ShouldBe("task-a");
    }

    [Fact]
    public void EntryWithTwoCategoryPaths_AppearsUnderBothBranches()
    {
        var library = new FakeTaskLibrary(ShellTask("task-a"));
        var entries = new[] { new TaskListEntry("task-a", ["a.b.c", "x.y.z"], null) };

        var root = CategoryTreeBuilder.Build(entries, library);

        root.Children["a"].Children["b"].Children["c"].Tasks.ShouldHaveSingleItem();
        root.Children["x"].Children["y"].Children["z"].Tasks.ShouldHaveSingleItem();
    }

    [Fact]
    public void SameDefinition_ReferencedTwiceWithDifferentParameterValues_ProducesTwoDistinctLeaves()
    {
        var library = new FakeTaskLibrary(ShellTask("delete-folder", new TaskParameterDefinition("FolderName", null, true, null)));
        var entries = new[]
        {
            new TaskListEntry("delete-folder", ["cleanup.a"], new Dictionary<string, string> { ["FolderName"] = "C:\\a" }),
            new TaskListEntry("delete-folder", ["cleanup.b"], new Dictionary<string, string> { ["FolderName"] = "C:\\b" }),
        };

        var root = CategoryTreeBuilder.Build(entries, library);

        root.Children["cleanup"].Children["a"].Tasks[0].ParameterValues["FolderName"].ShouldBe("C:\\a");
        root.Children["cleanup"].Children["b"].Tasks[0].ParameterValues["FolderName"].ShouldBe("C:\\b");
    }

    [Fact]
    public void DerivedDefinition_ReferencedWithEmptyParameterValues_ResolvesFully()
    {
        var baseDef = ShellTask("delete-folder", new TaskParameterDefinition("FolderName", null, true, null));
        var derived = new TaskDefinition(
            "delete-temp-folder", "Delete Temp Folder", null, "delete-folder",
            new Dictionary<string, string> { ["FolderName"] = "C:\\temp" }, null, null, null, null, null);
        var library = new FakeTaskLibrary(baseDef, derived);
        var entries = new[] { new TaskListEntry("delete-temp-folder", ["cleanup"], new Dictionary<string, string>()) };

        var root = CategoryTreeBuilder.Build(entries, library);

        root.Children["cleanup"].Tasks[0].ParameterValues["FolderName"].ShouldBe("C:\\temp");
    }

    [Fact]
    public void DerivedDefinition_ReferencedWithOverride_ResolvesToOverriddenValue()
    {
        var baseDef = ShellTask("delete-folder", new TaskParameterDefinition("FolderName", null, true, null));
        var derived = new TaskDefinition(
            "delete-temp-folder", "Delete Temp Folder", null, "delete-folder",
            new Dictionary<string, string> { ["FolderName"] = "C:\\temp" }, null, null, null, null, null);
        var library = new FakeTaskLibrary(baseDef, derived);
        var entries = new[]
        {
            new TaskListEntry("delete-temp-folder", ["cleanup"], new Dictionary<string, string> { ["FolderName"] = "D:\\scratch" }),
        };

        var root = CategoryTreeBuilder.Build(entries, library);

        root.Children["cleanup"].Tasks[0].ParameterValues["FolderName"].ShouldBe("D:\\scratch");
    }
}
