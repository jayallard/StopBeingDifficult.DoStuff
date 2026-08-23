using Microsoft.Extensions.Logging.Abstractions;
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

        var repository = CreateRepository(_directory);

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

        var repository = CreateRepository(_directory);

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

        Should.Throw<InvalidOperationException>(() => CreateRepository(_directory));
    }

    [Fact]
    public void MultipleDirectories_LoadsFromAll()
    {
        var secondDirectory = Directory.CreateTempSubdirectory("dostuff-tasklists-2-").FullName;
        try
        {
            WriteFile("a.yaml", """
                id: list-a
                name: List A
                entries: []
                """);
            File.WriteAllText(Path.Combine(secondDirectory, "b.yaml"), """
                id: list-b
                name: List B
                entries: []
                """);

            var repository = CreateRepository(_directory, secondDirectory);

            repository.GetAll().Count.ShouldBe(2);
        }
        finally
        {
            Directory.Delete(secondDirectory, recursive: true);
        }
    }

    [Fact]
    public void MissingDirectory_IsSkipped()
    {
        var missingDirectory = Path.Combine(_directory, "does-not-exist");
        WriteFile("a.yaml", """
            id: list-a
            name: List A
            entries: []
            """);

        var repository = CreateRepository(_directory, missingDirectory);

        repository.GetAll().Count.ShouldBe(1);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private void WriteFile(string name, string content) => File.WriteAllText(Path.Combine(_directory, name), content);

    private static YamlTaskListRepository CreateRepository(params string[] directories) =>
        new(directories, NullLogger<YamlTaskListRepository>.Instance);
}
