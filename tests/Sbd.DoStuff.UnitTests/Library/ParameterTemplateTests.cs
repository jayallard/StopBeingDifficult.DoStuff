using Sbd.DoStuff.Domain.Library;
using Shouldly;

namespace Sbd.DoStuff.UnitTests.Library;

public class ParameterTemplateTests
{
    [Fact]
    public void ReplacesToken_WithValue()
    {
        var result = ParameterTemplate.Substitute("rmdir {FolderName}", new Dictionary<string, string> { ["FolderName"] = "C:\\temp" });

        result.ShouldBe("rmdir C:\\temp");
    }

    [Fact]
    public void ReplacesMultipleOccurrences()
    {
        var result = ParameterTemplate.Substitute("{X} and {X}", new Dictionary<string, string> { ["X"] = "value" });

        result.ShouldBe("value and value");
    }

    [Fact]
    public void TemplateWithNoTokens_IsUnchanged()
    {
        var result = ParameterTemplate.Substitute("npm run build", new Dictionary<string, string>());

        result.ShouldBe("npm run build");
    }

    [Fact]
    public void UnknownToken_IsLeftLiteral()
    {
        var result = ParameterTemplate.Substitute("{Unknown}", new Dictionary<string, string>());

        result.ShouldBe("{Unknown}");
    }
}
