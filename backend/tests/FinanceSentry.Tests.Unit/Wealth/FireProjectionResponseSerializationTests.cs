namespace FinanceSentry.Tests.Unit.Wealth;

using System.Text.Json;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FluentAssertions;
using Xunit;

public class FireProjectionResponseSerializationTests
{
    [Theory]
    [InlineData(FireProjectionStatus.Projected, "Projected")]
    [InlineData(FireProjectionStatus.AlreadyReached, "AlreadyReached")]
    [InlineData(FireProjectionStatus.NotSaving, "NotSaving")]
    [InlineData(FireProjectionStatus.InsufficientHistory, "InsufficientHistory")]
    public void Status_is_written_by_name_so_the_tile_branches_on_a_state_not_an_ordinal(
        FireProjectionStatus status, string expected)
    {
        var response = new FireProjectionResponse(
            status, 0m, 0m, 0m, 0m, 0.04m, 0.05m, ProjectedDate: null, MonthsToFire: null, HasStaleSleeves: false);

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("status").GetString().Should().Be(expected);
    }
}
