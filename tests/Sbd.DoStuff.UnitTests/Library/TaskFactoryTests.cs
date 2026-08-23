using Sbd.DoStuff.Domain.Library;
using Sbd.DoStuff.Domain.Tasks;
using Shouldly;
using TaskFactory = Sbd.DoStuff.Domain.Library.TaskFactory;

namespace Sbd.DoStuff.UnitTests.Library;

public class TaskFactoryTests
{
    private static EffectiveTaskDefinition Effective(
        string? command, string? scriptPath = null, string? workingDirectory = null, IReadOnlyDictionary<string, string>? environmentVariables = null) =>
        new("task", "Task", null, "powershell", command, scriptPath, workingDirectory, environmentVariables, [],
            new Dictionary<string, string>(), new HashSet<string>());

    [Fact]
    public void PrependsVariableAssignment_ForEachParameter()
    {
        var factory = new TaskFactory();
        var effective = Effective("Remove-Item -Recurse -Force \"$FolderName\"");

        var task = (ShellCommandTask)factory.Create(effective, new Dictionary<string, string> { ["FolderName"] = @"C:\temp" });

        task.Command.ShouldBe("# --- Parameters ---\n$FolderName = 'C:\\temp'\n# --- End Parameters ---\nRemove-Item -Recurse -Force \"$FolderName\"");
    }

    [Fact]
    public void EscapesSingleQuotes_InParameterValues()
    {
        var factory = new TaskFactory();
        var effective = Effective("Write-Output $Message");

        var task = (ShellCommandTask)factory.Create(effective, new Dictionary<string, string> { ["Message"] = "it's a test" });

        task.Command.ShouldBe("# --- Parameters ---\n$Message = 'it''s a test'\n# --- End Parameters ---\nWrite-Output $Message");
    }

    [Fact]
    public void DoesNotSubstituteTokens_IntoCommandText()
    {
        var factory = new TaskFactory();
        var effective = Effective("Write-Output {FolderName}");

        var task = (ShellCommandTask)factory.Create(effective, new Dictionary<string, string> { ["FolderName"] = @"C:\temp" });

        task.Command.ShouldBe("# --- Parameters ---\n$FolderName = 'C:\\temp'\n# --- End Parameters ---\nWrite-Output {FolderName}");
    }

    [Fact]
    public void NoParameters_LeavesCommandUnchanged()
    {
        var factory = new TaskFactory();
        var effective = Effective("npm run build");

        var task = (ShellCommandTask)factory.Create(effective, new Dictionary<string, string>());

        task.Command.ShouldBe("npm run build");
    }

    [Fact]
    public void ScriptPath_BuildsCallOperatorInvocation_WithNamedArguments()
    {
        var factory = new TaskFactory();
        var effective = Effective(command: null, scriptPath: @"C:\scripts\cleanup.ps1");

        var task = (ShellCommandTask)factory.Create(effective, new Dictionary<string, string> { ["FolderName"] = @"C:\temp" });

        task.Command.ShouldBe(@"& 'C:\scripts\cleanup.ps1' -FolderName 'C:\temp'");
    }

    [Fact]
    public void ScriptPath_EscapesSingleQuotes_InArgumentValues()
    {
        var factory = new TaskFactory();
        var effective = Effective(command: null, scriptPath: @"C:\scripts\greet.ps1");

        var task = (ShellCommandTask)factory.Create(effective, new Dictionary<string, string> { ["Message"] = "it's a test" });

        task.Command.ShouldBe(@"& 'C:\scripts\greet.ps1' -Message 'it''s a test'");
    }

    [Fact]
    public void ScriptPath_NoParameters_HasNoTrailingArguments()
    {
        var factory = new TaskFactory();
        var effective = Effective(command: null, scriptPath: @"C:\scripts\cleanup.ps1");

        var task = (ShellCommandTask)factory.Create(effective, new Dictionary<string, string>());

        task.Command.ShouldBe(@"& 'C:\scripts\cleanup.ps1'");
    }

    [Fact]
    public void NeitherCommandNorScriptPath_Throws()
    {
        var factory = new TaskFactory();
        var effective = Effective(command: null);

        Should.Throw<InvalidOperationException>(() => factory.Create(effective, new Dictionary<string, string>()));
    }

    [Fact]
    public void StillSubstitutesTokens_InWorkingDirectoryAndEnvironmentVariables()
    {
        var factory = new TaskFactory();
        var effective = Effective(
            "npm run build",
            workingDirectory: "{RepoRoot}/frontend",
            environmentVariables: new Dictionary<string, string> { ["TARGET"] = "{Target}" });

        var task = (ShellCommandTask)factory.Create(
            effective, new Dictionary<string, string> { ["RepoRoot"] = @"C:\repo", ["Target"] = "prod" });

        task.WorkingDirectory.ShouldBe(@"C:\repo/frontend");
        task.EnvironmentVariables["TARGET"].ShouldBe("prod");
    }
}
