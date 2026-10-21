using Microsoft.Extensions.Logging.Abstractions;
using Sbd.DoStuff.Domain.Library;
using Shouldly;

namespace Sbd.DoStuff.UnitTests.Library;

public class YamlTaskLibraryTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dostuff-tasklibrary-").FullName;
    private readonly string _assetsDirectory = Directory.CreateTempSubdirectory("dostuff-assets-").FullName;

    [Fact]
    public void SingleItemArrayFile_LoadsOneDefinition()
    {
        WriteFile("a.yaml", """
            - id: task-a
              name: Task A
              type: powershell
              command: echo a
            """);

        var library = CreateLibrary([_directory], [_assetsDirectory]);

        library.Find("task-a").ShouldNotBeNull();
        library.GetAll().Count.ShouldBe(1);
    }

    [Fact]
    public void ParameterType_DefaultsToText_AndParsesMultilineText()
    {
        WriteFile("a.yaml", """
            - id: task-a
              name: Task A
              type: powershell
              parameters:
                - name: One
                - name: Many
                  type: multilineText
                - name: Path
                  type: filePath
              command: echo a
            """);

        var parameters = CreateLibrary([_directory], [_assetsDirectory]).Find("task-a")!.Parameters!;

        parameters.Single(p => p.Name == "One").Type.ShouldBe(TaskParameterType.Text);
        parameters.Single(p => p.Name == "Many").Type.ShouldBe(TaskParameterType.MultilineText);
        parameters.Single(p => p.Name == "Path").Type.ShouldBe(TaskParameterType.FilePath);
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

        var library = CreateLibrary([_directory], [_assetsDirectory]);

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

        Should.Throw<InvalidOperationException>(() => CreateLibrary([_directory], [_assetsDirectory]));
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

        Should.Throw<InvalidOperationException>(() => CreateLibrary([_directory], [_assetsDirectory]));
    }

    [Fact]
    public void RelativeScriptPath_ResolvesUnderAssetsDirectory()
    {
        File.WriteAllText(Path.Combine(_assetsDirectory, "cleanup.ps1"), "param($FolderName)");
        WriteFile("a.yaml", """
            - id: task-a
              name: Task A
              type: powershell
              scriptPath: cleanup.ps1
            """);

        var library = CreateLibrary([_directory], [_assetsDirectory]);

        library.Find("task-a")!.ScriptPath.ShouldBe(Path.Combine(_assetsDirectory, "cleanup.ps1"));
    }

    [Fact]
    public void AbsoluteScriptPath_UsedAsSpecified_IgnoringAssetsDirectory()
    {
        var outsideDirectory = Directory.CreateTempSubdirectory("dostuff-outside-").FullName;
        var scriptPath = Path.Combine(outsideDirectory, "cleanup.ps1");
        File.WriteAllText(scriptPath, "param($FolderName)");
        WriteFile("a.yaml", $"""
            - id: task-a
              name: Task A
              type: powershell
              scriptPath: {scriptPath}
            """);

        try
        {
            var library = CreateLibrary([_directory], [_assetsDirectory]);

            library.Find("task-a")!.ScriptPath.ShouldBe(scriptPath);
        }
        finally
        {
            Directory.Delete(outsideDirectory, recursive: true);
        }
    }

    [Fact]
    public void ScriptPath_NotFoundOnDisk_Throws()
    {
        WriteFile("a.yaml", """
            - id: task-a
              name: Task A
              type: powershell
              scriptPath: missing.ps1
            """);

        Should.Throw<InvalidOperationException>(() => CreateLibrary([_directory], [_assetsDirectory]));
    }

    [Fact]
    public void BothCommandAndScriptPath_Throws()
    {
        File.WriteAllText(Path.Combine(_assetsDirectory, "cleanup.ps1"), "param($FolderName)");
        WriteFile("a.yaml", """
            - id: task-a
              name: Task A
              type: powershell
              command: echo a
              scriptPath: cleanup.ps1
            """);

        Should.Throw<InvalidOperationException>(() => CreateLibrary([_directory], [_assetsDirectory]));
    }

    [Fact]
    public void DerivedDefinition_AlsoSettingScriptPath_Throws()
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
              scriptPath: cleanup.ps1
            """);

        Should.Throw<InvalidOperationException>(() => CreateLibrary([_directory], [_assetsDirectory]));
    }

    [Fact]
    public void MultipleDirectories_LoadsFromAll()
    {
        var secondDirectory = Directory.CreateTempSubdirectory("dostuff-tasklibrary-2-").FullName;
        try
        {
            WriteFile("a.yaml", """
                - id: task-a
                  name: Task A
                  type: powershell
                  command: echo a
                """);
            File.WriteAllText(Path.Combine(secondDirectory, "b.yaml"), """
                - id: task-b
                  name: Task B
                  type: powershell
                  command: echo b
                """);

            var library = CreateLibrary([_directory, secondDirectory], [_assetsDirectory]);

            library.GetAll().Count.ShouldBe(2);
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
            - id: task-a
              name: Task A
              type: powershell
              command: echo a
            """);

        var library = CreateLibrary([_directory, missingDirectory], [_assetsDirectory]);

        library.GetAll().Count.ShouldBe(1);
    }

    [Fact]
    public void RelativeScriptPath_ResolvesFromSecondAssetsDirectory_WhenNotInFirst()
    {
        var secondAssetsDirectory = Directory.CreateTempSubdirectory("dostuff-assets-2-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(secondAssetsDirectory, "cleanup.ps1"), "param($FolderName)");
            WriteFile("a.yaml", """
                - id: task-a
                  name: Task A
                  type: powershell
                  scriptPath: cleanup.ps1
                """);

            var library = CreateLibrary([_directory], [_assetsDirectory, secondAssetsDirectory]);

            library.Find("task-a")!.ScriptPath.ShouldBe(Path.Combine(secondAssetsDirectory, "cleanup.ps1"));
        }
        finally
        {
            Directory.Delete(secondAssetsDirectory, recursive: true);
        }
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        Directory.Delete(_assetsDirectory, recursive: true);
    }

    private void WriteFile(string name, string content) => File.WriteAllText(Path.Combine(_directory, name), content);

    [Fact]
    public void CreateGroup_WritesEmptyLibraryFile_AndReloadsCleanly()
    {
        var storage = Path.Combine(_directory, "new-root");
        var library = new YamlTaskLibrary([], [_assetsDirectory], NullLogger<YamlTaskLibrary>.Instance, [storage]);

        var group = library.CreateGroup("my-tools", storage);

        group.Tasks.ShouldBeEmpty();
        library.GetGroups().ShouldContain(g => g.Id == "my-tools");
        File.Exists(Path.Combine(storage, "my-tools.yaml")).ShouldBeTrue();
        CreateLibrary([storage], [_assetsDirectory]).GetGroups().Single().Id.ShouldBe("my-tools");
        Should.Throw<InvalidOperationException>(() => library.CreateGroup("my-tools", storage));
        Should.Throw<InvalidOperationException>(() => library.CreateGroup("other", _directory));
    }

    private static YamlTaskLibrary CreateLibrary(IEnumerable<string> directories, IEnumerable<string> assetsDirectories) =>
        new(directories, assetsDirectories, NullLogger<YamlTaskLibrary>.Instance);
}
