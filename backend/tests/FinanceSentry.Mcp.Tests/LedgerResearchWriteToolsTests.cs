using System.Security.Claims;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

/// <summary>
/// Origin stamping at the tool boundary: a call on a service token is Ledger's, a personal token or stdio (no HTTP
/// request) is the owner's. The tools hand that origin to the command, which journals it.
/// </summary>
public sealed class LedgerResearchWriteToolsTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static FakeIdentityResolver Identity => new() { ResolvedUserId = UserId };

    private static IHttpContextAccessor Caller(bool? serviceToken)
    {
        if (serviceToken is null)
            return new HttpContextAccessor();

        var claims = serviceToken.Value ? [new Claim("scope", "mcp.service")] : Array.Empty<Claim>();
        return new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) },
        };
    }

    [Theory]
    [InlineData(true, ThesisEventOrigin.Ledger)]
    [InlineData(false, ThesisEventOrigin.User)]
    [InlineData(null, ThesisEventOrigin.User)]
    public void CallerOrigin_IsLedgerOnlyForAServiceToken(bool? serviceToken, ThesisEventOrigin expected) =>
        McpCallerOrigin.Of(Caller(serviceToken)).Should().Be(expected);

    [Theory]
    [InlineData(true, ThesisEventOrigin.Ledger)]
    [InlineData(false, ThesisEventOrigin.User)]
    public async Task SaveThesis_StampsTheCallersOrigin_AndCarriesRationaleAndEvidence(bool serviceToken, ThesisEventOrigin expected)
    {
        var handler = new Mock<ICommandHandler<SaveThesisCommand, ThesisDto>>();
        SaveThesisCommand? captured = null;
        handler.Setup(h => h.Handle(It.IsAny<SaveThesisCommand>(), It.IsAny<CancellationToken>()))
            .Callback<SaveThesisCommand, CancellationToken>((c, _) => captured = c)
            .ReturnsAsync((ThesisDto)null!);
        var evidence = new ThesisEvidenceRefs(NewsArticleIds: ["n-1"], FundamentalsAsOf: "2026-09-30");

        await new SaveThesisTool(handler.Object, Identity, Caller(serviceToken))
            .ExecuteAsync("NVDA", "Case.", [], [], [], rationale: "Q2 beat", evidence: evidence);

        captured!.UserId.Should().Be(UserId);
        captured.Origin.Should().Be(expected);
        captured.Rationale.Should().Be("Q2 beat");
        captured.Evidence.Should().Be(evidence);
    }

    [Fact]
    public async Task PromoteAndReject_StampTheServiceTokenAsLedger()
    {
        var promote = new Mock<ICommandHandler<PromoteCandidateCommand, PromoteCandidateResult>>();
        PromoteCandidateCommand? promoted = null;
        promote.Setup(h => h.Handle(It.IsAny<PromoteCandidateCommand>(), It.IsAny<CancellationToken>()))
            .Callback<PromoteCandidateCommand, CancellationToken>((c, _) => promoted = c)
            .ReturnsAsync((PromoteCandidateResult)null!);
        var reject = new Mock<ICommandHandler<RejectCandidateCommand, RejectCandidateResult>>();
        RejectCandidateCommand? rejected = null;
        reject.Setup(h => h.Handle(It.IsAny<RejectCandidateCommand>(), It.IsAny<CancellationToken>()))
            .Callback<RejectCandidateCommand, CancellationToken>((c, _) => rejected = c)
            .ReturnsAsync(new RejectCandidateResult(true, CandidateStatus.Rejected));

        await new PromoteCandidateTool(promote.Object, Identity, Caller(true)).ExecuteAsync(Guid.NewGuid());
        await new RejectCandidateTool(reject.Object, Identity, Caller(true)).ExecuteAsync(Guid.NewGuid(), "too rich");

        promoted!.Origin.Should().Be(ThesisEventOrigin.Ledger);
        rejected!.Origin.Should().Be(ThesisEventOrigin.Ledger);
    }

    [Fact]
    public async Task Watchlist_AddAndRemove_ByServiceToken_ReachTheHandlers_StampedLedger()
    {
        var list = new Mock<IQueryHandler<GetWatchlistQuery, IReadOnlyList<WatchlistItemDto>>>();
        var add = new Mock<ICommandHandler<AddWatchlistItemCommand, WatchlistItemDto>>();
        AddWatchlistItemCommand? added = null;
        add.Setup(h => h.Handle(It.IsAny<AddWatchlistItemCommand>(), It.IsAny<CancellationToken>()))
            .Callback<AddWatchlistItemCommand, CancellationToken>((c, _) => added = c)
            .ReturnsAsync(new WatchlistItemDto(Guid.NewGuid(), "AMD", null, null, DateTimeOffset.UtcNow, "Ledger"));
        var remove = new Mock<ICommandHandler<RemoveWatchlistItemCommand, bool>>();
        RemoveWatchlistItemCommand? removed = null;
        remove.Setup(h => h.Handle(It.IsAny<RemoveWatchlistItemCommand>(), It.IsAny<CancellationToken>()))
            .Callback<RemoveWatchlistItemCommand, CancellationToken>((c, _) => removed = c)
            .ReturnsAsync(true);
        var tool = new WatchlistTool(list.Object, add.Object, remove.Object, Identity, Caller(true));

        await tool.ExecuteAsync("add", ticker: "AMD");
        await tool.ExecuteAsync("remove", itemId: Guid.NewGuid());

        added!.Origin.Should().Be(ThesisEventOrigin.Ledger);
        removed!.Origin.Should().Be(ThesisEventOrigin.Ledger);
    }

    [Fact]
    public async Task RevertThesisRevision_StampsTheCallersOrigin_AndPassesTheEventId()
    {
        var handler = new Mock<ICommandHandler<RevertThesisRevisionCommand, RevertThesisRevisionResult>>();
        RevertThesisRevisionCommand? captured = null;
        handler.Setup(h => h.Handle(It.IsAny<RevertThesisRevisionCommand>(), It.IsAny<CancellationToken>()))
            .Callback<RevertThesisRevisionCommand, CancellationToken>((c, _) => captured = c)
            .ReturnsAsync(new RevertThesisRevisionResult(RevertOutcome.Reverted, null));
        var eventId = Guid.NewGuid();

        await new RevertThesisRevisionTool(handler.Object, Identity, Caller(false)).ExecuteAsync(eventId, "undo");

        captured!.EventId.Should().Be(eventId);
        captured.Origin.Should().Be(ThesisEventOrigin.User);
        captured.Rationale.Should().Be("undo");
    }

    [Fact]
    public async Task Tools_ReturnNullWithoutAnIdentity()
    {
        var revert = new Mock<ICommandHandler<RevertThesisRevisionCommand, RevertThesisRevisionResult>>();

        (await new RevertThesisRevisionTool(revert.Object, new FakeIdentityResolver(), Caller(true)).ExecuteAsync(Guid.NewGuid()))
            .Should().BeNull();
        revert.VerifyNoOtherCalls();
    }
}
