using Sbd.DoStuff.Domain.Library;
using Sbd.DoStuff.Domain.Lists;
using Sbd.DoStuff.UnitTests.Fakes;
using Shouldly;

namespace Sbd.DoStuff.UnitTests.Lists;

public class TaskListConverterTests
{
    private static readonly TaskDefinition Base = new(
        "delete-folder", "Delete Folder", null, null, null, "powershell", "Remove-Item $FolderName", null, null, null,
        null);

    private static TaskListDefinition ListWith(params TaskListEntry[] entries) => new("list", "List", null, entries);

    [Fact]
    public void Plan_ClassifiesEntries()
    {
        var library = new FakeTaskLibrary(Base);
        var list = ListWith(
            new TaskListEntry("delete-folder", ["a"], new Dictionary<string, string> { ["FolderName"] = @"C:\temp" }),
            new TaskListEntry("delete-folder", ["a"], null),
            new TaskListEntry("missing", ["a"], null));

        var plan = new TaskListConverter(library, new FakeTaskListRepository(list)).Plan(list, "fake");

        plan.Select(p => p.Status).ShouldBe(
            [EntryConversionStatus.InTargetLibrary, EntryConversionStatus.InTargetLibrary, EntryConversionStatus.UnknownTask]);
    }

    [Fact]
    public void Convert_CreatesDerivedDefinition_AndRepointsEntry()
    {
        var library = new FakeTaskLibrary(Base);
        var entry = new TaskListEntry(
            "delete-folder", ["cleanup"], new Dictionary<string, string> { ["FolderName"] = @"C:\temp" },
            Notes: "keep", Name: "Delete Temp");
        var list = ListWith(entry);
        var lists = new FakeTaskListRepository(list);
        var converter = new TaskListConverter(library, lists);

        // Base lives in the only fake group, so "convert into" a group that doesn't hold it: use a second library.
        var other = new OtherGroupLibrary(library);
        converter = new TaskListConverter(other, lists);

        var plan = converter.Plan(list, "target");
        plan.Single().Status.ShouldBe(EntryConversionStatus.Convertible);

        converter.Convert("list", "target", [0]).ShouldBe(1);

        var created = other.Find("delete-temp").ShouldNotBeNull();
        created.BaseTaskId.ShouldBe("delete-folder");
        created.ParameterValues!["FolderName"].ShouldBe(@"C:\temp");
        created.Command.ShouldBeNull();

        var updated = lists.Find("list")!.Entries.Single();
        updated.TaskId.ShouldBe("delete-temp");
        updated.ParameterValues.ShouldBeNull();
        updated.Name.ShouldBeNull();
        updated.Categories.ShouldBe(["cleanup"]);
        updated.Notes.ShouldBe("keep");
    }

    [Fact]
    public void Convert_GeneratesUniqueIds_ForSameNamedEntries()
    {
        var library = new OtherGroupLibrary(new FakeTaskLibrary(Base));
        var list = ListWith(
            new TaskListEntry("delete-folder", ["a"], new Dictionary<string, string> { ["FolderName"] = "x" }),
            new TaskListEntry("delete-folder", ["a"], new Dictionary<string, string> { ["FolderName"] = "y" }));
        var lists = new FakeTaskListRepository(list);

        new TaskListConverter(library, lists).Convert("list", "target", [0, 1]).ShouldBe(2);

        lists.Find("list")!.Entries.Select(e => e.TaskId).ShouldBe(["delete-folder-2", "delete-folder-3"]);
    }

    private static readonly string PrivateFolder = Path.Combine(Path.GetTempPath(), "private", "TaskLibrary");
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "sys", "TaskLibrary");

    [Fact]
    public void Plan_RejectsTasksFromAnotherDataFolder()
    {
        var foreign = Path.Combine(Path.GetTempPath(), "tasksa", "TaskLibrary");
        var library = new TwoRootLibrary(foreign);
        var entry = new TaskListEntry("foreign-task", ["a"], new Dictionary<string, string> { ["X"] = "1" });
        var list = ListWith(entry);

        new TaskListConverter(library, new FakeTaskListRepository(list)).Plan(list, "target")
            .Single().Status.ShouldBe(EntryConversionStatus.OutsideSharedFolders);
    }

    private sealed class TwoRootLibrary(string foreignFolder) : ITaskLibrary
    {
        private readonly string _system = Path.Combine(Path.GetTempPath(), "sys", "TaskLibrary");
        private readonly TaskDefinition _task = Base with { Id = "foreign-task" };

        public IReadOnlyList<TaskDefinition> GetAll() => [_task];
        public IReadOnlyList<TaskLibraryGroup> GetGroups() =>
        [
            new("foreign", "Foreign", [_task], Path.Combine(foreignFolder, "foreign.yaml")),
            new("target", "Target", [], Path.Combine(Path.GetTempPath(), "tasksb", "TaskLibrary", "target.yaml")),
        ];
        public TaskDefinition? Find(string taskId) => taskId == _task.Id ? _task : null;
        public IReadOnlyList<string> StorageDirectories =>
            [_system, foreignFolder, Path.Combine(Path.GetTempPath(), "tasksb", "TaskLibrary")];
        public TaskLibraryGroup CreateGroup(string name, string storageDirectory) => throw new NotSupportedException();
        public void AddDefinitions(string groupId, IReadOnlyList<TaskDefinition> definitions) => throw new NotSupportedException();
    }

    // A library with an empty "target" group next to the group holding the base definitions.
    private sealed class OtherGroupLibrary(FakeTaskLibrary inner) : ITaskLibrary
    {
        private readonly List<TaskDefinition> _added = [];

        public IReadOnlyList<TaskDefinition> GetAll() => inner.GetAll().Concat(_added).ToList();

        public IReadOnlyList<TaskLibraryGroup> GetGroups() =>
            [.. inner.GetGroups().Select(g => g with { Source = Path.Combine(Folder, "fake.yaml") }), new TaskLibraryGroup("target", "Target", _added.ToList(), Path.Combine(PrivateFolder, "target.yaml"))];

        public TaskDefinition? Find(string taskId) => GetAll().FirstOrDefault(d => d.Id == taskId);

        public IReadOnlyList<string> StorageDirectories => [Folder, PrivateFolder];

        public TaskLibraryGroup CreateGroup(string name, string storageDirectory) => inner.CreateGroup(name, storageDirectory);

        public void AddDefinitions(string groupId, IReadOnlyList<TaskDefinition> definitions) => _added.AddRange(definitions);
    }
}
