using Sbd.DoStuff.Domain.Lists;
using Shouldly;

namespace Sbd.DoStuff.UnitTests.Lists;

public class JsonTaskListRepositoryTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dostuff-tasklists-").FullName;

    [Fact]
    public void SingleListFile_Loads()
    {
        WriteFile("a.json", """{ "id": "list-a", "name": "List A", "entries": [] }""");

        var repository = new JsonTaskListRepository(_directory);

        repository.Find("list-a").ShouldNotBeNull();
        repository.GetAll().Count.ShouldBe(1);
    }

    [Fact]
    public void MultipleListFiles_LoadAll()
    {
        WriteFile("a.json", """{ "id": "list-a", "name": "List A", "entries": [] }""");
        WriteFile("b.json", """{ "id": "list-b", "name": "List B", "entries": [] }""");

        var repository = new JsonTaskListRepository(_directory);

        repository.GetAll().Count.ShouldBe(2);
    }

    [Fact]
    public void DuplicateId_Throws()
    {
        WriteFile("a.json", """{ "id": "list-a", "name": "List A", "entries": [] }""");
        WriteFile("b.json", """{ "id": "list-a", "name": "List A Again", "entries": [] }""");

        Should.Throw<InvalidOperationException>(() => new JsonTaskListRepository(_directory));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private void WriteFile(string name, string content) => File.WriteAllText(Path.Combine(_directory, name), content);
}
