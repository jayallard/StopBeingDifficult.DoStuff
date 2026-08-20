using Sbd.DoStuff.Domain.Library;
using Shouldly;

namespace Sbd.DoStuff.UnitTests.Library;

public class JsonTaskLibraryTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dostuff-tasklibrary-").FullName;

    [Fact]
    public void SingleItemArrayFile_LoadsOneDefinition()
    {
        WriteFile("a.json", """[ { "id": "task-a", "name": "Task A", "type": "shell", "command": "echo a" } ]""");

        var library = new JsonTaskLibrary(_directory);

        library.Find("task-a").ShouldNotBeNull();
        library.GetAll().Count.ShouldBe(1);
    }

    [Fact]
    public void MultiItemArrayFile_LoadsAllDefinitions()
    {
        WriteFile("a.json", """
            [
              { "id": "task-a", "name": "Task A", "type": "shell", "command": "echo a" },
              { "id": "task-b", "name": "Task B", "type": "shell", "command": "echo b" }
            ]
            """);

        var library = new JsonTaskLibrary(_directory);

        library.GetAll().Count.ShouldBe(2);
    }

    [Fact]
    public void DuplicateId_AcrossFiles_Throws()
    {
        WriteFile("a.json", """[ { "id": "task-a", "name": "Task A", "type": "shell", "command": "echo a" } ]""");
        WriteFile("b.json", """[ { "id": "task-a", "name": "Task A Again", "type": "shell", "command": "echo a" } ]""");

        Should.Throw<InvalidOperationException>(() => new JsonTaskLibrary(_directory));
    }

    [Fact]
    public void DerivedDefinition_AlsoSettingCommand_Throws()
    {
        WriteFile("a.json", """[ { "id": "base", "name": "Base", "type": "shell", "command": "echo base" } ]""");
        WriteFile("b.json", """[ { "id": "derived", "name": "Derived", "baseTaskId": "base", "command": "echo derived" } ]""");

        Should.Throw<InvalidOperationException>(() => new JsonTaskLibrary(_directory));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private void WriteFile(string name, string content) => File.WriteAllText(Path.Combine(_directory, name), content);
}
