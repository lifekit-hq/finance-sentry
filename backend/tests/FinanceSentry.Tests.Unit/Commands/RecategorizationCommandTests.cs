namespace FinanceSentry.Tests.Unit.Commands;

using FinanceSentry.API.Commands;
using FinanceSentry.Modules.BankSync.Application.Services;
using FluentAssertions;
using Xunit;

public class RecategorizationCommandTests
{
    [Fact]
    public void ParseScope_NoFlags_ReturnsNullForTheFullPass()
    {
        RecategorizationCommand.ParseScope(["recategorize"]).Should().BeNull();
    }

    [Fact]
    public void ParseScope_UserIdOnly_ReturnsNullForTheFullPass()
    {
        RecategorizationCommand.ParseScope(["recategorize", Guid.NewGuid().ToString()]).Should().BeNull();
    }

    [Theory]
    [InlineData("recategorize", "--dry-run")]
    [InlineData("recategorize", "3f2504e0-4f89-11d3-9a0c-0305e82c3301", "--dry-run")]
    public void ParseScope_DryRunWithoutMove_Throws(params string[] args)
    {
        var act = () => RecategorizationCommand.ParseScope(args);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("recategorize", "--from", "RentAndUtilities")]
    [InlineData("recategorize", "--to", "Travel")]
    [InlineData("recategorize", "--from", "RentAndUtilities", "--dry-run")]
    [InlineData("recategorize", "--to", "Travel", "--dry-run")]
    [InlineData("recategorize", "--to", "Travel", "--from")]
    public void ParseScope_LoneFromOrTo_Throws(params string[] args)
    {
        var act = () => RecategorizationCommand.ParseScope(args);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ParseScope_FromAndTo_ReturnsLiveMove()
    {
        RecategorizationCommand.ParseScope(["recategorize", "--from", "RentAndUtilities", "--to", "Travel"])
            .Should().Be(new RecategorizationScope("RentAndUtilities", "Travel", DryRun: false));
    }

    [Fact]
    public void ParseScope_FromToAndDryRun_ReturnsPreviewMove()
    {
        RecategorizationCommand.ParseScope(
                ["recategorize", "3f2504e0-4f89-11d3-9a0c-0305e82c3301", "--dry-run", "--to", "Travel", "--from", "RentAndUtilities"])
            .Should().Be(new RecategorizationScope("RentAndUtilities", "Travel", DryRun: true));
    }
}
