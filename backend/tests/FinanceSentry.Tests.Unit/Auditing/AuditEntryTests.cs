namespace FinanceSentry.Tests.Unit.Auditing;

using FinanceSentry.Core.Auditing;
using FluentAssertions;
using Xunit;

/// <summary>
/// The audit trail records ids and field names, never values. <see cref="AuditEntry"/> is the only way onto the trail,
/// so these pin what it refuses: anything that is not an identifier cannot be a name on the trail.
/// </summary>
public sealed class AuditEntryTests
{
    private static readonly Guid Person = Guid.NewGuid();

    private static AuditEntry Entry(string resourceType = "income_source", IEnumerable<string>? fields = null, Guid? subject = null)
        => new(subject ?? Person, Person, AuditActorKind.OwnerSession, AuditAction.Update, resourceType, Guid.NewGuid(), fields);

    [Fact]
    public void FieldNames_AreSortedAndDistinct()
    {
        Entry(fields: ["Cadence", "Amount", "Cadence"]).FieldNames.Should().Equal("Amount", "Cadence");
    }

    [Fact]
    public void AnEntryWithNoFields_IsValid()
    {
        Entry(fields: null).FieldNames.Should().BeEmpty();
    }

    [Theory]
    [InlineData("5000")]
    [InlineData("4,500.00")]
    [InlineData("Acme Corp")]
    [InlineData("salary: 70000")]
    [InlineData("a@b.com")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("_Amount")]
    public void AValueShapedFieldName_IsRefused(string notAName)
    {
        var act = () => Entry(fields: [notAName]);

        act.Should().Throw<ArgumentException>().WithMessage("*never a value*");
    }

    [Fact]
    public void AFieldNameLongerThanTheLimit_IsRefused()
    {
        var act = () => Entry(fields: [new string('a', AuditEntry.MaxNameLength + 1)]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AValueShapedResourceType_IsRefused()
    {
        var act = () => Entry(resourceType: "my salary is 5000");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TooManyFieldNames_AreRefused()
    {
        var names = Enumerable.Range(0, AuditEntry.MaxFieldNames + 1).Select(i => $"Field{i}");

        var act = () => Entry(fields: names);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AnEntryNeedsAPerson()
    {
        var act = () => Entry(subject: Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AToolName_IsAValidResourceType()
    {
        Entry(resourceType: "get_financial_profile").ResourceType.Should().Be("get_financial_profile");
    }
}
