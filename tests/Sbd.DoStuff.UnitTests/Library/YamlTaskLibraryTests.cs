using Sbd.DoStuff.Domain.Library;
using Shouldly;

namespace Sbd.DoStuff.UnitTests.Library;

public class YamlTaskLibraryTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dostuff-tasklibrary-").FullName;

    [Fact]
    public void SingleItemArrayFile_LoadsOneDefinition()
    {
        WriteFile("a.yaml", """
            - id: task-a
              name: Task A
              type: powershell
              command: echo a
            """);

        var library = new YamlTaskLibrary(_directory);

        library.Find("task-a").ShouldNotBeNull();
        library.GetAll().Count.ShouldBe(1);
    }

    [Fact]
    public void MultiItemArrayFile_LoadsAllDefinitions()
    {
        WriteFile("a.yaml", """
            - id: task-a
              name: Task A
              type: powershell
              command: echo a
            - id: task-b
              name: Task B
              type: powershell
              command: echo b
            """);

        var library = new YamlTaskLibrary(_directory);

        library.GetAll().Count.ShouldBe(2);
    }

    [Fact]
    public void DuplicateId_AcrossFiles_Throws()
    {
        WriteFile("a.yaml", """
            - id: task-a
              name: Task A
              type: powershell
              command: echo a
            """);
        WriteFile("b.yaml", """
            - id: task-a
              name: Task A Again
              type: powershell
              command: echo a
            """);

        Should.Throw<InvalidOperationException>(() => new YamlTaskLibrary(_directory));
    }

    [Fact]
    public void DerivedDefinition_AlsoSettingCommand_Throws()
    {
        WriteFile("a.yaml", """
            - id: base
              name: Base
              type: powershell
              command: echo base
            """);
        WriteFile("b.yaml", """
            - id: derived
              name: Derived
              baseTaskId: base
              command: echo derived
            """);

        Should.Throw<InvalidOperationException>(() => new YamlTaskLibrary(_directory));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private void WriteFile(string name, string content) => File.WriteAllText(Path.Combine(_directory, name), content);
}
