using Sbd.DoStuff.Domain.Lists;
using Shouldly;

namespace Sbd.DoStuff.UnitTests.Lists;

public class YamlTaskListRepositoryTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dostuff-tasklists-").FullName;

    [Fact]
    public void SingleListFile_Loads()
    {
        WriteFile("a.yaml", """
            id: list-a
            name: List A
            entries: []
            """);

        var repository = new YamlTaskListRepository(_directory);

        repository.Find("list-a").ShouldNotBeNull();
        repository.GetAll().Count.ShouldBe(1);
    }

    [Fact]
    public void MultipleListFiles_LoadAll()
    {
        WriteFile("a.yaml", """
            id: list-a
            name: List A
            entries: []
            """);
        WriteFile("b.yaml", """
            id: list-b
            name: List B
            entries: []
            """);

        var repository = new YamlTaskListRepository(_directory);

        repository.GetAll().Count.ShouldBe(2);
    }

    [Fact]
    public void DuplicateId_Throws()
    {
        WriteFile("a.yaml", """
            id: list-a
            name: List A
            entries: []
            """);
        WriteFile("b.yaml", """
            id: list-a
            name: List A Again
            entries: []
            """);

        Should.Throw<InvalidOperationException>(() => new YamlTaskListRepository(_directory));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private void WriteFile(string name, string content) => File.WriteAllText(Path.Combine(_directory, name), content);
}
