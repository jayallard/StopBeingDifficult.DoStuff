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
}
