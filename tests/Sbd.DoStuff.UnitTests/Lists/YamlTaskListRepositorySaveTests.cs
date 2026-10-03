using Microsoft.Extensions.Logging.Abstractions;
using Sbd.DoStuff.Domain.Lists;
using Shouldly;

namespace Sbd.DoStuff.UnitTests.Lists;

public class YamlTaskListRepositorySaveTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("dostuff-tasklists-save-").FullName;
    private string PublicDir => Path.Combine(_root, "public");
    private string PrivateDir => Path.Combine(_root, "private");

    private YamlTaskListRepository CreateRepository() => new(
        [PublicDir, PrivateDir],
        NullLogger<YamlTaskListRepository>.Instance,
        new TaskListStorageOptions(PublicDir, PrivateDir));

    private static TaskListDefinition List(string id, string name = "Name") => new(
        id, name, "desc",
        [new TaskListEntry("task-a", ["a.b", "c"], new Dictionary<string, string> { ["P"] = "v" }),
         new TaskListEntry("task-b", ["a"], null)]);

    [Fact]
    public void Save_NewPrivate_WritesIntoPrivateDirectory_AndRoundTrips()
    {
        var repository = CreateRepository();

        repository.Save(List("mine"), TaskListScope.Private);

        File.Exists(Path.Combine(PrivateDir, "mine.yaml")).ShouldBeTrue();
        var reloaded = CreateRepository();
        reloaded.GetScope("mine").ShouldBe(TaskListScope.Private);
        var list = reloaded.Find("mine")!;
        list.Entries.Count.ShouldBe(2);
        list.Entries[0].Categories.ShouldBe(["a.b", "c"]);
        list.Entries[0].ParameterValues!["P"].ShouldBe("v");
        list.Entries[1].ParameterValues.ShouldBeNull();
    }

    [Fact]
    public void Save_NewPublic_WritesIntoPublicDirectory()
    {
        CreateRepository().Save(List("shared"), TaskListScope.Public);

        File.Exists(Path.Combine(PublicDir, "shared.yaml")).ShouldBeTrue();
        CreateRepository().GetScope("shared").ShouldBe(TaskListScope.Public);
    }

    [Fact]
    public void Save_ExistingList_RewritesItsOwnFile()
    {
        Directory.CreateDirectory(PublicDir);
        File.WriteAllText(Path.Combine(PublicDir, "oddly-named.yaml"), "id: x\nname: X\nentries: []\n");
        var repository = CreateRepository();

        repository.Save(List("x", "Renamed"), TaskListScope.Public, originalId: "x");

        Directory.GetFiles(PublicDir).Length.ShouldBe(1);
        CreateRepository().Find("x")!.Name.ShouldBe("Renamed");
    }

    [Fact]
    public void Save_ChangingScope_MovesFile()
    {
        var repository = CreateRepository();
        repository.Save(List("moving"), TaskListScope.Private);

        repository.Save(List("moving"), TaskListScope.Public, originalId: "moving");

        File.Exists(Path.Combine(PrivateDir, "moving.yaml")).ShouldBeFalse();
        File.Exists(Path.Combine(PublicDir, "moving.yaml")).ShouldBeTrue();
        repository.GetScope("moving").ShouldBe(TaskListScope.Public);
    }

    [Fact]
    public void Save_ChangingId_RenamesFile()
    {
        var repository = CreateRepository();
        repository.Save(List("old"), TaskListScope.Private);

        repository.Save(List("new"), TaskListScope.Private, originalId: "old");

        repository.Find("old").ShouldBeNull();
        repository.Find("new").ShouldNotBeNull();
        File.Exists(Path.Combine(PrivateDir, "old.yaml")).ShouldBeFalse();
        File.Exists(Path.Combine(PrivateDir, "new.yaml")).ShouldBeTrue();
    }

    [Fact]
    public void Save_DuplicateIdForNewList_Throws()
    {
        var repository = CreateRepository();
        repository.Save(List("dup"), TaskListScope.Private);

        Should.Throw<InvalidOperationException>(() => repository.Save(List("dup"), TaskListScope.Public));
    }

    [Fact]
    public void Save_InvalidId_Throws()
    {
        Should.Throw<ArgumentException>(() => CreateRepository().Save(List("../evil"), TaskListScope.Private));
    }

    [Fact]
    public void Delete_RemovesListAndFile()
    {
        var repository = CreateRepository();
        repository.Save(List("gone"), TaskListScope.Private);

        repository.Delete("gone");

        repository.Find("gone").ShouldBeNull();
        File.Exists(Path.Combine(PrivateDir, "gone.yaml")).ShouldBeFalse();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
