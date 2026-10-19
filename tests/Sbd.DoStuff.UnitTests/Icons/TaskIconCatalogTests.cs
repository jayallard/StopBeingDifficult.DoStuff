using Sbd.DoStuff.Domain.Icons;

namespace Sbd.DoStuff.UnitTests.Icons;

public sealed class TaskIconCatalogTests : IDisposable
{
    private readonly string _assets = Path.Combine(Path.GetTempPath(), "sbd-icons-" + Guid.NewGuid().ToString("N"));

    public TaskIconCatalogTests()
    {
        Directory.CreateDirectory(Path.Combine(_assets, "Icons"));
        File.WriteAllText(Path.Combine(_assets, "Icons", "default.svg"), "<svg/>");
        File.WriteAllText(Path.Combine(_assets, "Icons", "git.svg"), "<svg/>");
        File.WriteAllText(Path.Combine(_assets, "secret.svg"), "<svg/>");
    }

    public void Dispose() => Directory.Delete(_assets, recursive: true);

    [Fact]
    public void Resolve_ExistingIcon_ReturnsIt()
        => Assert.Equal("git", new TaskIconCatalog([_assets]).Resolve("git"));

    [Theory]
    [InlineData(null)]
    [InlineData("missing")]
    [InlineData("../secret")]
    public void Resolve_UnsetMissingOrEscaping_ReturnsDefault(string? icon)
        => Assert.Equal("default", new TaskIconCatalog([_assets]).Resolve(icon));

    [Fact]
    public void Resolve_UsesConfiguredDefault()
        => Assert.Equal("git", new TaskIconCatalog([_assets], "git").Resolve(null));

    [Fact]
    public void TryFindFile_AcceptsExplicitExtension()
        => Assert.True(new TaskIconCatalog([_assets]).TryFindFile("git.svg", out _));

    [Fact]
    public void GetAll_ListsIconFilesOnly()
        => Assert.Equal(["default", "git"], new TaskIconCatalog([_assets]).GetAll().Select(i => i.Name));

    [Theory]
    [InlineData("git.svg", "git")]
    [InlineData("git", "git")]
    [InlineData(" Git.PNG ", "Git")]
    public void NormalizeName_StripsImageExtension(string icon, string expected)
        => Assert.Equal(expected, TaskIconCatalog.NormalizeName(icon));

    [Theory]
    [InlineData("new-icon", true)]
    [InlineData("new.svg", true)]
    [InlineData("", false)]
    [InlineData("../evil", false)]
    [InlineData("a/b", false)]
    [InlineData(".hidden", false)]
    public void CanCreate_AcceptsOnlyPlainNames(string icon, bool expected)
        => Assert.Equal(expected, new TaskIconCatalog([_assets]).CanCreate(icon));

    [Fact]
    public void Create_WritesSvgThatIsThenFound()
    {
        var catalog = new TaskIconCatalog([_assets]);

        var path = catalog.Create("brand-new");

        Assert.Equal(Path.Combine(_assets, "Icons", "brand-new.svg"), path);
        Assert.True(catalog.TryFindFile("brand-new", out _));
        Assert.Contains("<svg", File.ReadAllText(path));
    }

    [Fact]
    public void Create_MakesIconsFolderWhenMissing()
    {
        var empty = Path.Combine(_assets, "other");
        Directory.CreateDirectory(empty);
        var catalog = new TaskIconCatalog([empty]);

        catalog.Create("x");

        Assert.True(catalog.TryFindFile("x", out _));
    }

    [Fact]
    public void Create_ExistingIcon_Throws()
        => Assert.Throws<InvalidOperationException>(() => new TaskIconCatalog([_assets]).Create("git"));

    [Fact]
    public void Add_WritesGivenContentWithExtension()
    {
        var catalog = new TaskIconCatalog([_assets]);

        var path = catalog.Add("custom", ".PNG", [1, 2, 3]);

        Assert.Equal(Path.Combine(_assets, "Icons", "custom.png"), path);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
    }

    [Fact]
    public void Add_UnsupportedExtension_Throws()
        => Assert.Throws<ArgumentException>(() => new TaskIconCatalog([_assets]).Add("custom", ".exe", [1]));

    [Fact]
    public void Delete_RemovesFile()
    {
        var catalog = new TaskIconCatalog([_assets]);

        catalog.Delete("git");

        Assert.False(catalog.TryFindFile("git", out _));
    }

    [Fact]
    public void Delete_DefaultOrMissing_Throws()
    {
        var catalog = new TaskIconCatalog([_assets]);

        Assert.Throws<InvalidOperationException>(() => catalog.Delete("default"));
        Assert.Throws<InvalidOperationException>(() => catalog.Delete("nope"));
    }

    [Fact]
    public void Create_InvalidName_Throws()
        => Assert.Throws<ArgumentException>(() => new TaskIconCatalog([_assets]).Create("../evil"));
}
