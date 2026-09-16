namespace FinanceSentry.Modules.Research.Tests.Unit;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class GenerateAssetLedgerReadCommandTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IQueryHandler<GetAssetDossierQuery, AssetDossierResult>> _dossier = new();
    private readonly Mock<ILedgerNarrator> _narrator = new();
    private readonly Mock<IAssetLedgerReadRepository> _repository = new();

    public GenerateAssetLedgerReadCommandTests() =>
        _dossier
            .Setup(d => d.Handle(It.IsAny<GetAssetDossierQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssetDossierResult(
                "CBRS", null, null, null, null, [], null, [], DateTimeOffset.UtcNow));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_Throws503_AndPersistsNothing_WhenNarratorHasNoAnswer(string? narrative)
    {
        // #635: a failed agent turn must fail visibly and never be cached as the asset's read.
        _narrator.Setup(n => n.NarrateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(narrative);

        var act = () => CreateSut().Handle(new GenerateAssetLedgerReadCommand(UserId, "cbrs", Force: true), default);

        (await act.Should().ThrowAsync<LedgerReadUnavailableException>()).Which.StatusCode.Should().Be(503);
        _repository.Verify(r => r.UpsertAsync(It.IsAny<AssetLedgerRead>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PersistsTrimmedNarrative_WhenNarratorAnswers()
    {
        _narrator.Setup(n => n.NarrateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("  CBRS is a small position.  ");

        var result = await CreateSut().Handle(new GenerateAssetLedgerReadCommand(UserId, "cbrs", Force: true), default);

        result.Symbol.Should().Be("CBRS");
        result.Narrative.Should().Be("CBRS is a small position.");
        result.Cached.Should().BeFalse();
        _repository.Verify(
            r => r.UpsertAsync(
                It.Is<AssetLedgerRead>(x => x.Symbol == "CBRS" && x.Narrative == "CBRS is a small position."),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private GenerateAssetLedgerReadCommandHandler CreateSut() =>
        new(_dossier.Object, _narrator.Object, _repository.Object);
}
