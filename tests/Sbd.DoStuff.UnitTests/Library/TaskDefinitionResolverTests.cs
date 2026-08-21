using Sbd.DoStuff.Domain.Library;
using Sbd.DoStuff.UnitTests.Fakes;
using Shouldly;

namespace Sbd.DoStuff.UnitTests.Library;

public class TaskDefinitionResolverTests
{
    private static TaskDefinition ShellTask(string id, params TaskParameterDefinition[] parameters) =>
        new(id, id, null, null, null, "shell", $"echo {id}", null, null, parameters);

    private static TaskDefinition Derived(string id, string baseTaskId, IReadOnlyDictionary<string, string> pinned) =>
        new(id, id, null, baseTaskId, pinned, null, null, null, null, null);

    private static TaskDefinition Derived(
        string id, string baseTaskId, IReadOnlyDictionary<string, string> pinned, IReadOnlyDictionary<string, bool> canOverride) =>
        new(id, id, null, baseTaskId, pinned, null, null, null, null, null, canOverride);

    [Fact]
    public void SingleLevelInheritance_PinsParameter_ButKeepsItInParametersList()
    {
        var baseDef = ShellTask("delete-folder", new TaskParameterDefinition("FolderName", null, true, null));
        var derived = Derived("delete-temp-folder", "delete-folder", new Dictionary<string, string> { ["FolderName"] = "C:\\temp" });
        var library = new FakeTaskLibrary(baseDef, derived);

        var effective = TaskDefinitionResolver.Resolve(derived, library);

        effective.Parameters.ShouldContain(p => p.Name == "FolderName");
        effective.PinnedParameterValues["FolderName"].ShouldBe("C:\\temp");
        effective.Type.ShouldBe("shell");
        effective.Command.ShouldBe("echo delete-folder");
    }

    [Fact]
    public void MultiLevelChain_Flattens_WithMostDerivedPinWinning()
    {
        var root = ShellTask("root", new TaskParameterDefinition("X", null, false, null));
        var mid = Derived("mid", "root", new Dictionary<string, string> { ["X"] = "mid-value" });
        var leaf = Derived("leaf", "mid", new Dictionary<string, string> { ["X"] = "leaf-value" });
        var library = new FakeTaskLibrary(root, mid, leaf);

        var effective = TaskDefinitionResolver.Resolve(leaf, library);

        effective.PinnedParameterValues["X"].ShouldBe("leaf-value");
        effective.Type.ShouldBe("shell");
    }

    [Fact]
    public void LeafNameAndDescription_WinOverBase()
    {
        var baseDef = new TaskDefinition("base", "Base Name", "Base Description", null, null, "shell", "echo hi", null, null, []);
        var derived = new TaskDefinition(
            "derived", "Derived Name", "Derived Description", "base", new Dictionary<string, string>(),
            null, null, null, null, null);
        var library = new FakeTaskLibrary(baseDef, derived);

        var effective = TaskDefinitionResolver.Resolve(derived, library);

        effective.Name.ShouldBe("Derived Name");
        effective.Description.ShouldBe("Derived Description");
    }

    [Fact]
    public void UnresolvableBaseTaskId_Throws()
    {
        var derived = Derived("derived", "missing-base", new Dictionary<string, string>());
        var library = new FakeTaskLibrary(derived);

        Should.Throw<TaskDefinitionCycleException>(() => TaskDefinitionResolver.Resolve(derived, library));
    }

    [Fact]
    public void Cycle_Throws()
    {
        var a = Derived("a", "b", new Dictionary<string, string>());
        var b = Derived("b", "a", new Dictionary<string, string>());
        var library = new FakeTaskLibrary(a, b);

        Should.Throw<TaskDefinitionCycleException>(() => TaskDefinitionResolver.Resolve(a, library));
    }

    [Fact]
    public void ParameterDefault_CanOverride_IsTrue_WhenUnspecified()
    {
        var baseDef = ShellTask("delete-folder", new TaskParameterDefinition("FolderName", null, true, null));
        var library = new FakeTaskLibrary(baseDef);

        var effective = TaskDefinitionResolver.Resolve(baseDef, library);

        effective.NonOverridableParameterNames.ShouldNotContain("FolderName");
    }

    [Fact]
    public void BaseParameter_CanOverrideFalse_MarksParameterNonOverridable()
    {
        var baseDef = ShellTask("delete-folder", new TaskParameterDefinition("FolderName", null, true, null, CanOverride: false));
        var library = new FakeTaskLibrary(baseDef);

        var effective = TaskDefinitionResolver.Resolve(baseDef, library);

        effective.NonOverridableParameterNames.ShouldContain("FolderName");
    }

    [Fact]
    public void DerivedDefinition_CanOverrideFalse_MarksParameterNonOverridable()
    {
        var baseDef = ShellTask("delete-folder", new TaskParameterDefinition("FolderName", null, true, null));
        var derived = Derived(
            "delete-temp-folder", "delete-folder",
            new Dictionary<string, string> { ["FolderName"] = "C:\\temp" },
            new Dictionary<string, bool> { ["FolderName"] = false });
        var library = new FakeTaskLibrary(baseDef, derived);

        var effective = TaskDefinitionResolver.Resolve(derived, library);

        effective.NonOverridableParameterNames.ShouldContain("FolderName");
    }

    [Fact]
    public void MoreDerivedLevel_CannotReenable_ParameterLockedFalseByBase()
    {
        var baseDef = ShellTask("delete-folder", new TaskParameterDefinition("FolderName", null, true, null, CanOverride: false));
        var derived = Derived(
            "delete-temp-folder", "delete-folder",
            new Dictionary<string, string> { ["FolderName"] = "C:\\temp" },
            new Dictionary<string, bool> { ["FolderName"] = true });
        var library = new FakeTaskLibrary(baseDef, derived);

        var effective = TaskDefinitionResolver.Resolve(derived, library);

        effective.NonOverridableParameterNames.ShouldContain("FolderName");
    }
}
