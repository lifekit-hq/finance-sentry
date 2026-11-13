namespace FinanceSentry.Modules.Research.Tests.Persistence;

using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>A watchlist change by Ledger is journaled with its origin, and the list read shows who added each entry.</summary>
public sealed class LedgerWatchlistAuditTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed class ListWatchlistRepository : IWatchlistRepository
    {
        public List<WatchlistItem> Items { get; } = [];

        public Task<IReadOnlyList<WatchlistItem>> ListAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WatchlistItem>>(Items.Where(i => i.UserId == userId).ToList());

        public Task<IReadOnlyList<WatchlistItem>> ListUnscopedAsync(Guid userId, CancellationToken ct = default)
            => ListAsync(userId, ct);

        public Task<WatchlistItem?> FindAsync(Guid userId, string ticker, CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(i => i.UserId == userId && i.Ticker == ticker));

        public Task<WatchlistItem?> FindByIdAsync(Guid userId, Guid itemId, CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(i => i.UserId == userId && i.Id == itemId));

        public Task AddAsync(WatchlistItem item, CancellationToken ct = default)
        {
            Items.Add(item);
            return Task.CompletedTask;
        }

        public Task<bool> RemoveAsync(Guid userId, Guid itemId, CancellationToken ct = default)
            => Task.FromResult(Items.RemoveAll(i => i.UserId == userId && i.Id == itemId) > 0);
    }

    private static (ListWatchlistRepository Repo, InMemoryThesisEventRepository Journal, IThesisEventRecorder Recorder) Build()
    {
        var journal = new InMemoryThesisEventRepository();
        var quotes = new Mock<IMarketDataService>();
        quotes.Setup(q => q.GetQuotesAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, QuoteCacheEntry>());
        return (new ListWatchlistRepository(), journal, new ThesisEventRecorder(journal, quotes.Object, NullLogger<ThesisEventRecorder>.Instance));
    }

    [Fact]
    public async Task Add_ByLedger_JournalsACreatedWatchlistEvent_WithLedgerOrigin_AndTheListShowsIt()
    {
        var (repo, journal, recorder) = Build();
        var add = new AddWatchlistItemCommandHandler(repo, recorder, new InlineResearchWriteScope(), NullLogger<AddWatchlistItemCommandHandler>.Instance);

        var added = await add.Handle(
            new AddWatchlistItemCommand(UserId, " amd ", "NASDAQ", "supply-chain read-through", ThesisEventOrigin.Ledger),
            CancellationToken.None);

        added.Origin.Should().Be("Ledger");
        var row = journal.Rows.Should().ContainSingle().Subject;
        row.SubjectType.Should().Be(ThesisSubjectType.Watchlist);
        row.SubjectId.Should().Be(added.Id);
        row.EventType.Should().Be(ThesisEventType.Created);
        row.Ticker.Should().Be("AMD");
        row.Origin.Should().Be(ThesisEventOrigin.Ledger);
        row.Audit!.Rationale.Should().Be("supply-chain read-through");

        var listed = await new GetWatchlistQueryHandler(repo, new ThesisOriginReader(journal))
            .Handle(new GetWatchlistQuery(UserId), CancellationToken.None);
        listed.Should().ContainSingle().Which.Origin.Should().Be("Ledger");
    }

    [Fact]
    public async Task Add_ByOwner_ReadsAsUser()
    {
        var (repo, journal, recorder) = Build();
        var add = new AddWatchlistItemCommandHandler(repo, recorder, new InlineResearchWriteScope(), NullLogger<AddWatchlistItemCommandHandler>.Instance);

        await add.Handle(new AddWatchlistItemCommand(UserId, "MSFT", null, null), CancellationToken.None);

        (await new GetWatchlistQueryHandler(repo, new ThesisOriginReader(journal))
            .Handle(new GetWatchlistQuery(UserId), CancellationToken.None))
            .Should().ContainSingle().Which.Origin.Should().Be("User");
    }

    [Fact]
    public async Task Add_Duplicate_StillRefused_ForLedger()
    {
        var (repo, _, recorder) = Build();
        var add = new AddWatchlistItemCommandHandler(repo, recorder, new InlineResearchWriteScope(), NullLogger<AddWatchlistItemCommandHandler>.Instance);
        await add.Handle(new AddWatchlistItemCommand(UserId, "AMD", null, null, ThesisEventOrigin.Ledger), CancellationToken.None);

        var again = () => add.Handle(
            new AddWatchlistItemCommand(UserId, "amd", null, null, ThesisEventOrigin.Ledger), CancellationToken.None);

        await again.Should().ThrowAsync<WatchlistItemAlreadyExistsException>();
    }

    [Fact]
    public async Task Remove_ByLedger_JournalsAClosedWatchlistEvent_ForTheRemovedEntry()
    {
        var (repo, journal, recorder) = Build();
        var added = await new AddWatchlistItemCommandHandler(repo, recorder, new InlineResearchWriteScope(), NullLogger<AddWatchlistItemCommandHandler>.Instance)
            .Handle(new AddWatchlistItemCommand(UserId, "AMD", null, "thesis broke"), CancellationToken.None);

        var removed = await new RemoveWatchlistItemCommandHandler(repo, recorder, new InlineResearchWriteScope(), NullLogger<RemoveWatchlistItemCommandHandler>.Instance)
            .Handle(new RemoveWatchlistItemCommand(UserId, added.Id, ThesisEventOrigin.Ledger), CancellationToken.None);

        removed.Should().BeTrue();
        repo.Items.Should().BeEmpty();
        var closed = journal.Rows.Should().Contain(e => e.EventType == ThesisEventType.Closed).Subject;
        closed.SubjectType.Should().Be(ThesisSubjectType.Watchlist);
        closed.SubjectId.Should().Be(added.Id);
        closed.Origin.Should().Be(ThesisEventOrigin.Ledger);
        closed.Ticker.Should().Be("AMD");
    }

    [Fact]
    public async Task Remove_UnknownItem_ReturnsFalse_AndJournalsNothing()
    {
        var (repo, journal, recorder) = Build();

        var removed = await new RemoveWatchlistItemCommandHandler(repo, recorder, new InlineResearchWriteScope(), NullLogger<RemoveWatchlistItemCommandHandler>.Instance)
            .Handle(new RemoveWatchlistItemCommand(UserId, Guid.NewGuid(), ThesisEventOrigin.Ledger), CancellationToken.None);

        removed.Should().BeFalse();
        journal.Rows.Should().BeEmpty();
    }
}
