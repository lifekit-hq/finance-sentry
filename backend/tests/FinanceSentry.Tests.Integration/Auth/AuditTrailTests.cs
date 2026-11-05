namespace FinanceSentry.Tests.Integration.Auth;

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinanceSentry.Core.Auditing;
using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Auth;
using FinanceSentry.Modules.Auth.Application.Commands;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// The audit trail against real Postgres: what <c>auth.audit_events</c> stores (ids and field names), that a person sees
/// only the events concerning their own data, and that the person's events leave with the account (erasure) and come
/// with their data download (export) - and nobody else's do.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AuditTrailTests : IAsyncLifetime
{
    private readonly Guid _personA = Guid.NewGuid();
    private readonly Guid _personB = Guid.NewGuid();
    private TestDatabase? _database;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();
        await using var setup = CreateContext();
        await setup.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    private AuthDbContext CreateContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(actingUser));

    // The real Auth module wiring, acting for the given person (null: a job).
    private ServiceProvider CreateServices(Guid? actingUser = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _database!.ConnectionString,
            ["Jwt:Secret"] = "audit-trail-tests-signing-secret-0123456789-abcdef",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ICurrentUser>(new FixedCurrentUser(actingUser));
        services.AddAuthModule(config);
        return services.BuildServiceProvider();
    }

    private static AuditEntry Entry(Guid subject, string[] fields, AuditAction action = AuditAction.Update)
        => new(subject, subject, AuditActorKind.OwnerSession, action, "income_source", Guid.NewGuid(), fields);

    private async Task RecordAsync(params AuditEntry[] entries)
    {
        await using var services = CreateServices();
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuditTrail>().RecordAsync(entries);
    }

    [DockerRequiredFact]
    public async Task Record_StoresIdsAndFieldNames_ForTheSubject()
    {
        using var activity = new Activity("audit-test").Start();
        var entry = new AuditEntry(_personA, _personA, AuditActorKind.InAppAgent, AuditAction.Read, "get_financial_profile", null, ["Cadence", "Amount"]);

        await RecordAsync(entry);

        await using var db = CreateContext();
        var stored = await db.AuditEvents.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking().SingleAsync();
        stored.SubjectUserId.Should().Be(_personA);
        stored.ActorUserId.Should().Be(_personA);
        stored.ActorKind.Should().Be(AuditActorKind.InAppAgent);
        stored.Action.Should().Be(AuditAction.Read);
        stored.ResourceType.Should().Be("get_financial_profile");
        stored.ResourceId.Should().BeNull();
        stored.FieldNames.Should().Equal("Amount", "Cadence");
        stored.TraceId.Should().Be(activity.TraceId.ToHexString());
        stored.OccurredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [DockerRequiredFact]
    public async Task ABatch_IsStoredTogether()
    {
        await RecordAsync(Entry(_personA, ["Amount"]), Entry(_personA, ["Label", "Amount"], AuditAction.Create));

        await using var db = CreateContext();
        (await db.AuditEvents.IgnoreQueryFilters([OwnerQueryFilter.Name]).CountAsync()).Should().Be(2);
    }

    [DockerRequiredFact]
    public async Task EachPerson_SeesOnlyTheEventsConcerningTheirOwnData()
    {
        await RecordAsync(Entry(_personA, ["Amount"]), Entry(_personB, ["Label"]));

        await using (var asA = CreateContext(_personA))
            (await asA.AuditEvents.Select(e => e.SubjectUserId).ToListAsync()).Should().Equal(_personA);

        await using (var asB = CreateContext(_personB))
        {
            (await asB.AuditEvents.Select(e => e.SubjectUserId).ToListAsync()).Should().Equal(_personB);
            (await asB.AuditEvents.AnyAsync(e => e.SubjectUserId == _personA)).Should().BeFalse(
                "the filter holds even when a query names another person explicitly");
        }

        await using var asNoOne = CreateContext();
        (await asNoOne.AuditEvents.AnyAsync()).Should().BeFalse("no person in scope matches no row");
    }

    [DockerRequiredFact]
    public async Task TheList_IsNewestFirst_PagedAndScopedToTheAsker()
    {
        for (var i = 0; i < 5; i++)
        {
            await RecordAsync(Entry(_personA, [$"Field{i}"]));
            await Task.Delay(5);
        }

        await RecordAsync(Entry(_personB, ["Other"]));

        await using var db = CreateContext(_personA);
        var handler = new ListAuditEventsQueryHandler(db);

        var first = await handler.Handle(new ListAuditEventsQuery(_personA, Take: 2), default);
        first.Events.Select(e => e.FieldNames.Single()).Should().Equal("Field4", "Field3");
        first.NextBefore.Should().NotBeNull();

        var second = await handler.Handle(new ListAuditEventsQuery(_personA, first.NextBefore, first.NextBeforeId, 2), default);
        second.Events.Select(e => e.FieldNames.Single()).Should().Equal("Field2", "Field1");

        var last = await handler.Handle(new ListAuditEventsQuery(_personA, second.NextBefore, second.NextBeforeId, 2), default);
        last.Events.Select(e => e.FieldNames.Single()).Should().Equal("Field0");
        last.NextBefore.Should().BeNull();
        last.NextBeforeId.Should().BeNull();
    }

    [DockerRequiredFact]
    public async Task APageBoundary_InsideOneBatch_LosesNoEvent()
    {
        const int batchSize = 7;
        const int pageSize = 3;
        var fields = Enumerable.Range(0, batchSize).Select(i => $"Field{i}").ToArray();
        await RecordAsync(fields.Select(field => Entry(_personA, [field])).ToArray());

        await using var db = CreateContext(_personA);
        var handler = new ListAuditEventsQueryHandler(db);

        (await db.AuditEvents.Select(e => e.OccurredAt).Distinct().CountAsync()).Should().Be(1, "a batch is stamped with one time");

        var seen = new List<string>();
        var page = await handler.Handle(new ListAuditEventsQuery(_personA, Take: pageSize), default);
        seen.AddRange(page.Events.Select(e => e.FieldNames.Single()));
        while (page.NextBefore is not null)
        {
            page = await handler.Handle(new ListAuditEventsQuery(_personA, page.NextBefore, page.NextBeforeId, pageSize), default);
            seen.AddRange(page.Events.Select(e => e.FieldNames.Single()));
        }

        seen.Should().BeEquivalentTo(fields).And.OnlyHaveUniqueItems();
    }

    [DockerRequiredFact]
    public async Task TheCursor_ReturnedByTheApi_PagesOnOverHttp()
    {
        const int batchSize = 5;
        const int pageSize = 2;
        var fields = Enumerable.Range(0, batchSize).Select(i => $"Field{i}").ToArray();
        await RecordAsync(fields.Select(field => Entry(_personA, [field])).ToArray());

        await using var factory = new PostgresApiFactory(_database!.ConnectionString);
        factory.CreateClient().Dispose();
        await factory.CreateAccountAsync(_personA, AuthRoles.Member);
        using var client = await factory.ClientForAsync(_personA);

        var seen = new List<string>();
        var url = $"/api/v1/me/audit-events?take={pageSize}";
        while (true)
        {
            var response = await client.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var page = (await response.Content.ReadFromJsonAsync<PageShape>())!;
            seen.AddRange(page.Events.Select(e => e.FieldNames.Single()));
            if (page.NextBefore is null)
                break;
            url = $"/api/v1/me/audit-events?take={pageSize}&before={Uri.EscapeDataString(page.NextBefore)}&beforeId={page.NextBeforeId}";
        }

        seen.Should().BeEquivalentTo(fields).And.OnlyHaveUniqueItems();
    }

    private sealed record PageShape(List<EventShape> Events, string? NextBefore, Guid? NextBeforeId);

    private sealed record EventShape(string[] FieldNames);

    [DockerRequiredFact]
    public async Task Erasure_RemovesTheEventsConcerningThePerson_AndOnlyThose()
    {
        await RecordAsync(Entry(_personA, ["Amount"]), Entry(_personA, [], AuditAction.Delete), Entry(_personB, ["Label"]));

        await using var services = CreateServices();
        using var scope = services.CreateScope();
        var eraser = scope.ServiceProvider.GetServices<IUserDataEraser>().Single(e => e.Module == "Auth");
        var removed = await eraser.EraseAsync(_personA);

        removed.Should().BeGreaterThanOrEqualTo(2);
        await using var db = CreateContext();
        var left = await db.AuditEvents.IgnoreQueryFilters([OwnerQueryFilter.Name]).Select(e => e.SubjectUserId).ToListAsync();
        left.Should().Equal(_personB);
    }

    [DockerRequiredFact]
    public async Task TheExport_HoldsThePersonsEvents_AndNothingOfAnyoneElse()
    {
        await RecordAsync(Entry(_personA, ["Amount"]), Entry(_personB, ["Label"]));

        await using var services = CreateServices();
        using var scope = services.CreateScope();
        var exporter = scope.ServiceProvider.GetServices<IUserDataExporter>().Single(e => e.Module == "Auth");
        using var stream = new MemoryStream();
        await using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            await exporter.ExportAsync(_personA, writer);
            writer.WriteEndObject();
        }

        var text = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        using var document = JsonDocument.Parse(text);
        var events = document.RootElement.GetProperty("audit_events");
        events.GetArrayLength().Should().Be(1);
        events[0].GetProperty("fieldNames").EnumerateArray().Select(f => f.GetString()).Should().Equal("Amount");
        text.Should().NotContain(_personB.ToString());
    }
}
