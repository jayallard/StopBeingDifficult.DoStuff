using Sbd.DoStuff.Domain.Icons;
using Sbd.DoStuff.Domain.Library;
using Sbd.DoStuff.Domain.Lists;
using Sbd.DoStuff.UnitTests.Fakes;

namespace Sbd.DoStuff.UnitTests.Icons;

public sealed class IconUsageScannerTests : IDisposable
{
    private readonly string _assets = Path.Combine(Path.GetTempPath(), "sbd-scan-" + Guid.NewGuid().ToString("N"));

    public IconUsageScannerTests() => Directory.CreateDirectory(Path.Combine(_assets, "Icons"));

    public void Dispose() => Directory.Delete(_assets, recursive: true);

    private static TaskDefinition Definition(string id, string? icon)
        => new(id, id.ToUpperInvariant(), null, null, null, "powershell", "x", null, null, null, null, Icon: icon);

    private static TaskListDefinition List(params TaskListEntry[] entries) => new("l1", "List One", null, entries);

    private IReadOnlyList<IconReference> Scan(TaskDefinition[] definitions, params TaskListDefinition[] lists)
        => new IconUsageScanner(new FakeTaskLibrary(definitions), new FakeTaskListRepository(lists), new TaskIconCatalog([_assets]))
            .Scan();

    [Fact]
    public void Scan_MergesDefinitionAndEntryUsagesUnderNormalisedName()
    {
        var result = Scan(
            [Definition("a", "git.svg")],
            List(new TaskListEntry("a", ["x"], null, Icon: "GIT")));

        var git = Assert.Single(result, r => r.Name.Equals("git", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, git.Usages.Count);
        Assert.Contains(git.Usages, u => u.Description == "Definition: A" && u.Link is null);
        Assert.Contains(git.Usages, u => u.Description == "List: List One, entry: A" && u.Link == "lists/l1");
    }

    [Fact]
    public void Scan_IgnoresTasksWithoutIcon_ButIncludesLibraryDefault()
    {
        var result = Scan([Definition("a", null)]);

        var only = Assert.Single(result);
        Assert.Equal("default", only.Name);
        Assert.Equal("Library default icon", Assert.Single(only.Usages).Description);
    }

    [Fact]
    public void Scan_EntryNameOverridesDefinitionName()
    {
        var result = Scan(
            [Definition("a", null)],
            List(new TaskListEntry("a", ["x"], null, Name: "Custom", Icon: "foo")));

        Assert.Contains("entry: Custom", Assert.Single(Assert.Single(result, r => r.Name == "foo").Usages).Description);
    }
}
