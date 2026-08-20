using Sbd.DoStuff.Domain.Library;
using Shouldly;

namespace Sbd.DoStuff.UnitTests.Library;

public class TaskParameterResolverTests
{
    private static EffectiveTaskDefinition Effective(
        TaskParameterDefinition parameter, IReadOnlyDictionary<string, string>? pinned = null) =>
        new("task", "Task", null, "shell", "echo {X}", null, null, [parameter], pinned ?? new Dictionary<string, string>());

    [Fact]
    public void SuppliedValue_WinsOverPinnedAndDefault()
    {
        var parameter = new TaskParameterDefinition("X", null, Required: false, DefaultValue: "default");
        var effective = Effective(parameter, new Dictionary<string, string> { ["X"] = "pinned" });

        var resolved = TaskParameterResolver.Resolve(effective, new Dictionary<string, string> { ["X"] = "supplied" });

        resolved["X"].ShouldBe("supplied");
    }

    [Fact]
    public void PinnedValue_WinsOverDefault_WhenNotSupplied()
    {
        var parameter = new TaskParameterDefinition("X", null, Required: false, DefaultValue: "default");
        var effective = Effective(parameter, new Dictionary<string, string> { ["X"] = "pinned" });

        var resolved = TaskParameterResolver.Resolve(effective, null);

        resolved["X"].ShouldBe("pinned");
    }

    [Fact]
    public void DefaultValue_Used_WhenNothingSuppliedOrPinned()
    {
        var parameter = new TaskParameterDefinition("X", null, Required: false, DefaultValue: "default");
        var effective = Effective(parameter);

        var resolved = TaskParameterResolver.Resolve(effective, null);

        resolved["X"].ShouldBe("default");
    }

    [Fact]
    public void Required_MissingEverywhere_Throws()
    {
        var parameter = new TaskParameterDefinition("X", null, Required: true, DefaultValue: null);
        var effective = Effective(parameter);

        Should.Throw<MissingRequiredParameterException>(() => TaskParameterResolver.Resolve(effective, null));
    }

    [Fact]
    public void Optional_MissingEverywhere_ResolvesToEmptyString()
    {
        var parameter = new TaskParameterDefinition("X", null, Required: false, DefaultValue: null);
        var effective = Effective(parameter);

        var resolved = TaskParameterResolver.Resolve(effective, null);

        resolved["X"].ShouldBe(string.Empty);
    }

    [Fact]
    public void SuppliedValue_CanOverride_AlreadyPinnedParameter()
    {
        var parameter = new TaskParameterDefinition("X", null, Required: true, DefaultValue: null);
        var effective = Effective(parameter, new Dictionary<string, string> { ["X"] = "pinned" });

        var resolved = TaskParameterResolver.Resolve(effective, new Dictionary<string, string> { ["X"] = "overridden" });

        resolved["X"].ShouldBe("overridden");
    }
}
