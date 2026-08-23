using Microsoft.Extensions.Logging.Abstractions;
using Sbd.DoStuff.Domain.Library;
using Shouldly;

namespace Sbd.DoStuff.UnitTests.Library;

public class YamlTaskLibraryTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dostuff-tasklibrary-").FullName;
    private readonly string _artifactsDirectory = Directory.CreateTempSubdirectory("dostuff-artifacts-").FullName;

    [Fact]
    public void SingleItemArrayFile_LoadsOneDefinition()
    {
        WriteFile("a.yaml", """
            - id: task-a
              name: Task A
              type: powershell
              command: echo a
            """);

        var library = CreateLibrary([_directory], [_artifactsDirectory]);

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

        var library = CreateLibrary([_directory], [_artifactsDirectory]);

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

        Should.Throw<InvalidOperationException>(() => CreateLibrary([_directory], [_artifactsDirectory]));
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

        Should.Throw<InvalidOperationException>(() => CreateLibrary([_directory], [_artifactsDirectory]));
    }

    [Fact]
    public void RelativeScriptPath_ResolvesUnderArtifactsDirectory()
    {
        File.WriteAllText(Path.Combine(_artifactsDirectory, "cleanup.ps1"), "param($FolderName)");
        WriteFile("a.yaml", """
            - id: task-a
              name: Task A
              type: powershell
              scriptPath: cleanup.ps1
            """);

        var library = CreateLibrary([_directory], [_artifactsDirectory]);

        library.Find("task-a")!.ScriptPath.ShouldBe(Path.Combine(_artifactsDirectory, "cleanup.ps1"));
    }

    [Fact]
    public void AbsoluteScriptPath_UsedAsSpecified_IgnoringArtifactsDirectory()
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
            var library = CreateLibrary([_directory], [_artifactsDirectory]);

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

        Should.Throw<InvalidOperationException>(() => CreateLibrary([_directory], [_artifactsDirectory]));
    }

    [Fact]
    public void BothCommandAndScriptPath_Throws()
    {
        File.WriteAllText(Path.Combine(_artifactsDirectory, "cleanup.ps1"), "param($FolderName)");
        WriteFile("a.yaml", """
            - id: task-a
              name: Task A
              type: powershell
              command: echo a
              scriptPath: cleanup.ps1
            """);

        Should.Throw<InvalidOperationException>(() => CreateLibrary([_directory], [_artifactsDirectory]));
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

        Should.Throw<InvalidOperationException>(() => CreateLibrary([_directory], [_artifactsDirectory]));
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

            var library = CreateLibrary([_directory, secondDirectory], [_artifactsDirectory]);

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

        var library = CreateLibrary([_directory, missingDirectory], [_artifactsDirectory]);

        library.GetAll().Count.ShouldBe(1);
    }

    [Fact]
    public void RelativeScriptPath_ResolvesFromSecondArtifactsDirectory_WhenNotInFirst()
    {
        var secondArtifactsDirectory = Directory.CreateTempSubdirectory("dostuff-artifacts-2-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(secondArtifactsDirectory, "cleanup.ps1"), "param($FolderName)");
            WriteFile("a.yaml", """
                - id: task-a
                  name: Task A
                  type: powershell
                  scriptPath: cleanup.ps1
                """);

            var library = CreateLibrary([_directory], [_artifactsDirectory, secondArtifactsDirectory]);

            library.Find("task-a")!.ScriptPath.ShouldBe(Path.Combine(secondArtifactsDirectory, "cleanup.ps1"));
        }
        finally
        {
            Directory.Delete(secondArtifactsDirectory, recursive: true);
        }
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        Directory.Delete(_artifactsDirectory, recursive: true);
    }

    private void WriteFile(string name, string content) => File.WriteAllText(Path.Combine(_directory, name), content);

    private static YamlTaskLibrary CreateLibrary(IEnumerable<string> directories, IEnumerable<string> artifactsDirectories) =>
        new(directories, artifactsDirectories, NullLogger<YamlTaskLibrary>.Instance);
}
