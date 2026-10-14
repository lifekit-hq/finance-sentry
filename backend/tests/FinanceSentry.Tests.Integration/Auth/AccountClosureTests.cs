namespace FinanceSentry.Tests.Integration.Auth;

using System.Net;
using System.Net.Http.Json;
using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Infrastructure.Jobs;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

/// <summary>
/// Closing an account through the API and reopening it, against a real database: the last Owner cannot close, a
/// closed account shows on the People page with its purge date, an Owner can reopen it during the grace period and
/// a Member cannot, and the purge leaves a reopened account alone.
/// </summary>
[Trait("Category", "Integration")]
[Collection(PostgresClusterStateCollection.Name)]
public sealed class AccountClosureTests : IAsyncLifetime
{
    private TestDatabase? _database;

    public async Task InitializeAsync() =>
        _database = await PostgresServer.Postgres14.CreateDatabaseAsync(resetsClusterRoles: true);

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    [DockerRequiredFact]
    public async Task TheLastOwner_CannotClose_AMemberCloses_AndAnOwnerReopensBeforeThePurge()
    {
        await using var factory = new PostgresApiFactory(_database!.ConnectionString);
        factory.CreateClient().Dispose();

        var owner = Guid.NewGuid();
        var member = Guid.NewGuid();
        await factory.CreateAccountAsync(owner, AuthRoles.Owner);
        await factory.CreateAccountAsync(member, AuthRoles.Member);
        using var ownerClient = await factory.ClientForAsync(owner);

        var ownerOptions = await ownerClient.GetFromJsonAsync<OptionsShape>("/api/v1/account/closure");
        ownerOptions!.CanClose.Should().BeFalse();
        ownerOptions.Reason.Should().Contain("only owner");
        var refused = await ownerClient.PostAsJsonAsync("/api/v1/account/close", new { confirmEmail = $"{owner}@test.local" });
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadFromJsonAsync<ErrorShape>())!.ErrorCode.Should().Be("LAST_OWNER_CANNOT_CLOSE");

        using (var memberClient = await factory.ClientForAsync(member))
        {
            var memberOptions = await memberClient.GetFromJsonAsync<OptionsShape>("/api/v1/account/closure");
            memberOptions.Should().Be(new OptionsShape(true, null, 30));

            (await memberClient.PostAsync($"/api/v1/people/{member}/reopen", null)).StatusCode
                .Should().Be(HttpStatusCode.Forbidden, "only someone who manages people can reopen an account");

            var close = await memberClient.PostAsJsonAsync("/api/v1/account/close", new { confirmEmail = $"{member}@test.local" });
            close.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var people = await ownerClient.GetFromJsonAsync<List<PersonShape>>("/api/v1/people");
        var closed = people!.Single(p => p.Id == member.ToString());
        closed.Status.Should().Be("Closed");
        closed.PurgeDueAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(5));
        people!.Single(p => p.Id == owner.ToString()).PurgeDueAt.Should().BeNull();

        (await ownerClient.PostAsync($"/api/v1/people/{member}/reopen", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var again = await ownerClient.PostAsync($"/api/v1/people/{member}/reopen", null);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await again.Content.ReadFromJsonAsync<ErrorShape>())!.ErrorCode.Should().Be("ACCOUNT_NOT_CLOSED");

        using (var reopened = await factory.ClientForAsync(member))
            (await reopened.GetAsync("/api/v1/account/closure")).StatusCode.Should().Be(HttpStatusCode.OK, "a reopened account works again");

        (await ownerClient.GetFromJsonAsync<List<PersonShape>>("/api/v1/people"))!
            .Single(p => p.Id == member.ToString()).Status.Should().NotBe("Closed");

        // Even once the original purge date has passed, a reopened closure is never purged.
        await using var conn = new NpgsqlConnection(_database.ConnectionString);
        await conn.OpenAsync();
        await using (var due = new NpgsqlCommand("UPDATE auth.\"AccountClosures\" SET \"PurgeDueAt\" = now() - interval '1 minute'", conn))
            await due.ExecuteNonQueryAsync();
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AccountPurgeJob>().ExecuteAsync();

        await using var count = new NpgsqlCommand("SELECT count(*) FROM auth.\"AspNetUsers\" WHERE \"Id\" = @id", conn);
        count.Parameters.AddWithValue("id", member.ToString());
        ((long)(await count.ExecuteScalarAsync())!).Should().Be(1);
    }

    [DockerRequiredFact]
    public async Task ClosingSomeoneElsesAccount_IsRefused_AndClosesNothing()
    {
        await using var factory = new PostgresApiFactory(_database!.ConnectionString);
        factory.CreateClient().Dispose();

        var owner = Guid.NewGuid();
        var member = Guid.NewGuid();
        await factory.CreateAccountAsync(owner, AuthRoles.Owner);
        await factory.CreateAccountAsync(member, AuthRoles.Member);

        using var memberClient = await factory.ClientForAsync(member);
        // A target smuggled into the body is not part of the contract: the action is always on the caller.
        var response = await memberClient.PostAsJsonAsync(
            "/api/v1/account/close", new { confirmEmail = $"{owner}@test.local", userId = owner });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ErrorShape>())!.ErrorCode.Should().Be("CLOSURE_CONFIRMATION_MISMATCH");

        using var ownerClient = await factory.ClientForAsync(owner);
        (await ownerClient.GetAsync("/api/v1/account/closure")).StatusCode.Should().Be(HttpStatusCode.OK, "the endpoint acts on the caller only");
        (await memberClient.GetAsync("/api/v1/account/closure")).StatusCode.Should().Be(HttpStatusCode.OK, "a refused confirmation closes nothing");
    }

    private sealed record OptionsShape(bool CanClose, string? Reason, int GraceDays);

    private sealed record PersonShape(string Id, string Email, IReadOnlyList<string> Roles, string Status, DateTime? PurgeDueAt);

    private sealed record ErrorShape(string Error, string ErrorCode);
}
