namespace FinanceSentry.Modules.Research.Tests.Coverage;

using FinanceSentry.Modules.Research.Application.Services.Coverage;
using FluentAssertions;
using Xunit;

public sealed class CoverageMemoParserTests
{
    private const string Good = """
        {
          "business": "Sells memory.",
          "whyNow": "Two Buy readings.",
          "falsifiers": [
            {"kind": "metric", "statement": "Revenue growth turns negative", "metric": "revenue_yoy", "direction": "below", "threshold": 0},
            {"kind": "narrative", "statement": "HBM share loss"}
          ],
          "crowding": "Normal.",
          "correlationToBook": "0.56 to DRAM.",
          "verdict": "watch",
          "caveats": "No peer data."
        }
        """;

    [Fact]
    public void TryParse_ReadsTheMemoShape_AndNormalisesTheVerdict()
    {
        var memo = CoverageMemoParser.TryParse(Good);

        memo.Should().NotBeNull();
        memo!.Verdict.Should().Be(CoverageMemoParser.Watch);
        memo.Falsifiers.Should().HaveCount(2);
        memo.Falsifiers[0].Should().Be(new CoverageMemoFalsifier("metric", "Revenue growth turns negative", "revenue_yoy", "below", 0m));
        memo.Falsifiers[1].Kind.Should().Be("narrative");
    }

    [Fact]
    public void TryParse_ReadsAReplyWrappedInProseAndFences()
    {
        var memo = CoverageMemoParser.TryParse($"Here is the memo:\n```json\n{Good}\n```\n");

        memo.Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("no json here")]
    [InlineData("{ not json }")]
    public void TryParse_ReturnsNull_ForAReplyThatIsNotJson(string reply)
        => CoverageMemoParser.TryParse(reply).Should().BeNull();

    [Fact]
    public void TryParse_ReturnsNull_ForAVerdictOutsideTheVocabulary()
        => CoverageMemoParser.TryParse(Good.Replace("\"watch\"", "\"Strong Buy\"")).Should().BeNull();

    [Fact]
    public void TryParse_ReturnsNull_WhenThereIsNoFalsifier()
        => CoverageMemoParser.TryParse("""{"business":"b","whyNow":"w","falsifiers":[],"verdict":"Buy"}""").Should().BeNull();

    [Fact]
    public void TryParse_ReturnsNull_WhenTheBusinessIsMissing()
        => CoverageMemoParser.TryParse(Good.Replace("\"business\": \"Sells memory.\",", string.Empty)).Should().BeNull();

    [Theory]
    [InlineData("\"metric\": \"vibes\", \"direction\": \"below\", \"threshold\": 1")]
    [InlineData("\"metric\": \"revenue_yoy\", \"direction\": \"sideways\", \"threshold\": 1")]
    [InlineData("\"metric\": \"revenue_yoy\", \"direction\": \"below\"")]
    public void TryParse_KeepsAnUnevaluableMetricFalsifier_AsNarrative(string tail)
    {
        var reply = Good.Replace(
            "\"metric\": \"revenue_yoy\", \"direction\": \"below\", \"threshold\": 0", tail);

        var memo = CoverageMemoParser.TryParse(reply);

        memo!.Falsifiers[0].Should().Be(new CoverageMemoFalsifier("narrative", "Revenue growth turns negative"));
    }

    [Fact]
    public void Serialize_RoundTripsThroughDeserialize()
    {
        var memo = CoverageMemoParser.TryParse(Good)!;

        CoverageMemoParser.Deserialize(CoverageMemoParser.Serialize(memo)).Should().BeEquivalentTo(memo);
        CoverageMemoParser.Deserialize("not json").Should().BeNull();
    }
}
