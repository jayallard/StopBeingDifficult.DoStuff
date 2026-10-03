using Sbd.DoStuff.Domain.Library;
using Sbd.DoStuff.Domain.Lists;
using Sbd.DoStuff.UnitTests.Fakes;
using Shouldly;

namespace Sbd.DoStuff.UnitTests.Lists;

public class TaskListValidatorTests
{
    private static readonly FakeTaskLibrary Library = new(
        new TaskDefinition("t", "T", null, null, null, "powershell", "echo", null, null, null,
            [new TaskParameterDefinition("Req", null, true, null), new TaskParameterDefinition("Locked", null, false, "x", CanOverride: false)]));

    private static TaskListEntry Entry(string task, string[] categories, Dictionary<string, string>? values) => new(task, categories, values);

    private static TaskListDefinition ListOf(params TaskListEntry[] entries) => new("id", "Name", null, entries);

    [Fact]
    public void ValidList_HasNoErrors() =>
        TaskListValidator.Validate(ListOf(Entry("t", ["a.b"], new Dictionary<string, string> { ["Req"] = "1" })), Library)
            .ShouldBeEmpty();

    [Fact]
    public void UnknownTask_IsReported() =>
        TaskListValidator.Validate(ListOf(Entry("nope", ["a"], null)), Library).ShouldHaveSingleItem();

    [Fact]
    public void MissingRequiredParameter_IsReported() =>
        TaskListValidator.Validate(ListOf(Entry("t", ["a"], null)), Library).ShouldHaveSingleItem();

    [Fact]
    public void OverridingLockedParameter_IsReported() =>
        TaskListValidator.Validate(
            ListOf(Entry("t", ["a"], new Dictionary<string, string> { ["Req"] = "1", ["Locked"] = "y" })), Library)
            .ShouldHaveSingleItem();

    [Theory]
    [InlineData("a..b")]
    [InlineData(".a")]
    [InlineData("a.")]
    public void BadCategory_IsReported(string category) =>
        TaskListValidator.Validate(
            ListOf(Entry("t", [category], new Dictionary<string, string> { ["Req"] = "1" })), Library)
            .ShouldHaveSingleItem();

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("../x")]
    public void BadId_IsInvalid(string id) => TaskListValidator.IsValidId(id).ShouldBeFalse();
}
