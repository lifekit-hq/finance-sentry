namespace FinanceSentry.Tests.Integration.Auth;

using System.Security.Claims;
using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth;
using FinanceSentry.Modules.Auth.Application.Commands;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using FinanceSentry.Modules.Auth.Infrastructure.Services;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// The Owner query filter on <see cref="AuthDbContext"/>: the per-user credential rows (<see cref="RefreshToken"/>,
/// <see cref="McpAuthorizationCode"/>) are visible only to the person they belong to, while Identity's own tables stay
/// unfiltered. Sign-in, token refresh, sign-out and the MCP code exchange all run with no person in scope, so the
/// credential lookups opt out explicitly and match on the token or code hash; a bulk revoke names its user and works
/// whoever is acting. Real Postgres is required, matching the other filter suites.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AuthOwnerQueryFilterTests : IAsyncLifetime
{
    private const string Password = "quiet lantern orchard";
    private const string RedirectUri = "http://127.0.0.1:33418/callback";

    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(1);

    // Identity's own tables carry a UserId but are read before a person is in scope (sign-in, refresh, the
    // per-request principal load, the role seed) and across users (managing people), so they stay unfiltered.
    // McpServiceToken is not filtered yet: its revocation check runs while the MCP host is still authenticating.
    private static readonly Type[] DeliberatelyUnfiltered =
    [
        typeof(IdentityUserClaim<string>), typeof(IdentityUserLogin<string>),
        typeof(IdentityUserRole<string>), typeof(IdentityUserToken<string>),
        typeof(McpServiceToken),
    ];

    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();
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

    // Null acts as an anonymous request or a background job: no person in scope.
    private AuthDbContext CreateContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(actingUser));

    // The real Auth module wiring (Identity, token services, stores) against the test database, acting for the
    // given person; null is an anonymous request such as sign-in, refresh or the MCP token exchange.
    private ServiceProvider CreateServices(Guid? actingUser = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _database!.ConnectionString,
            ["Jwt:Secret"] = "auth-owner-query-filter-tests-signing-secret-0123456789",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ICurrentUser>(new FixedCurrentUser(actingUser));
        services.AddAuthModule(config);
        return services.BuildServiceProvider();
    }

    private static T Create<T>(IServiceScope scope) => ActivatorUtilities.CreateInstance<T>(scope.ServiceProvider);

    private async Task CreateUserAsync(Guid id, string email)
    {
        await using var services = CreateServices();
        using var scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var result = await users.CreateAsync(new ApplicationUser { Id = id.ToString(), UserName = email, Email = email }, Password);
        result.Succeeded.Should().BeTrue(string.Join(", ", result.Errors.Select(e => e.Description)));
    }

    private async Task<string> IssueRefreshTokenAsync(Guid userId)
    {
        // Inserts are not filtered, so a no-person context writes any user's rows.
        await using var ctx = CreateContext();
        var (raw, _) = await new RefreshTokenService(ctx).IssueAsync(userId.ToString());
        return raw;
    }

    private async Task<List<RefreshToken>> AllRefreshTokensAsync()
    {
        await using var read = CreateContext();
        return await read.RefreshTokens.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking().ToListAsync();
    }

    [Fact]
    public void Every_per_user_credential_entity_declares_the_Owner_filter()
    {
        using var ctx = CreateContext();

        var perUser = ctx.Model.GetEntityTypes()
            .Where(e => e.FindProperty("UserId") is not null && !DeliberatelyUnfiltered.Contains(e.ClrType))
            .ToList();

        perUser.Select(e => e.ClrType).Should().BeEquivalentTo([typeof(RefreshToken), typeof(McpAuthorizationCode)]);
        perUser.Should().OnlyContain(
            e => e.GetDeclaredQueryFilters().Any(f => f.Key == OwnerQueryFilter.Name),
            "a per-user entity without the Owner filter would be readable across people");
        ctx.Model.GetEntityTypes().Where(e => DeliberatelyUnfiltered.Contains(e.ClrType) || e.ClrType == typeof(ApplicationUser))
            .Should().OnlyContain(e => !e.GetDeclaredQueryFilters().Any(), "sign-in and people management read these across users");
    }

    [DockerRequiredFact]
    public async Task Each_person_sees_only_their_own_credentials_and_no_person_sees_none()
    {
        await IssueRefreshTokenAsync(_userA);
        await IssueRefreshTokenAsync(_userB);
        await using (var seed = CreateContext())
        {
            await new PersistedMcpAuthorizationCodeStore(seed).IssueAsync(_userA.ToString(), "a@test.com", RedirectUri);
            await new PersistedMcpAuthorizationCodeStore(seed).IssueAsync(_userB.ToString(), "b@test.com", RedirectUri);
        }

        await using (var asA = CreateContext(_userA))
        {
            (await asA.RefreshTokens.Select(t => t.UserId).ToListAsync()).Should().Equal(_userA.ToString());
            (await asA.McpAuthorizationCodes.Select(c => c.UserId).ToListAsync()).Should().Equal(_userA.ToString());
        }

        await using (var asB = CreateContext(_userB))
        {
            (await asB.RefreshTokens.Select(t => t.UserId).ToListAsync()).Should().Equal(_userB.ToString());
            (await asB.RefreshTokens.AnyAsync(t => t.UserId == _userA.ToString())).Should().BeFalse(
                "the filter holds even when a query names another person explicitly");
            (await asB.McpAuthorizationCodes.Select(c => c.UserId).ToListAsync()).Should().Equal(_userB.ToString());
        }

        await using var asNoOne = CreateContext();
        (await asNoOne.RefreshTokens.AnyAsync()).Should().BeFalse("no person in scope matches no row");
        (await asNoOne.McpAuthorizationCodes.AnyAsync()).Should().BeFalse("no person in scope matches no row");
    }

    [DockerRequiredFact]
    public async Task Sign_in_refresh_and_sign_out_with_no_person_find_the_presented_token()
    {
        await CreateUserAsync(_userA, "signin-a@test.com");
        await using var services = CreateServices();

        AuthResult signedIn;
        using (var scope = services.CreateScope())
            signedIn = await Create<LoginCommandHandler>(scope).Handle(new LoginCommand("signin-a@test.com", Password), default);

        AuthResult refreshed;
        using (var scope = services.CreateScope())
            refreshed = await Create<RefreshCommandHandler>(scope).Handle(new RefreshCommand(signedIn.RawRefreshToken), default);

        refreshed.Response.User.Id.Should().Be(_userA.ToString());
        using (var scope = services.CreateScope())
        {
            var refreshTokens = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
            (await refreshTokens.ValidateUnscopedAsync(signedIn.RawRefreshToken)).Should().BeNull("a refresh rotates the token");
            (await refreshTokens.ValidateUnscopedAsync(refreshed.RawRefreshToken)).Should().NotBeNull();
        }

        using (var scope = services.CreateScope())
            await Create<LogoutCommandHandler>(scope).Handle(new LogoutCommand(refreshed.RawRefreshToken), default);

        (await AllRefreshTokensAsync()).Should().HaveCount(2).And.OnlyContain(t => t.IsRevoked);
    }

    [DockerRequiredFact]
    public async Task Mcp_code_exchange_refresh_and_revoke_with_no_person_find_the_presented_code_and_token()
    {
        await CreateUserAsync(_userA, "mcp-a@test.com");
        await using var services = CreateServices();

        string code;
        using (var scope = services.CreateScope())
            code = await scope.ServiceProvider.GetRequiredService<IMcpAuthorizationCodeStore>()
                .IssueAsync(_userA.ToString(), "mcp-a@test.com", RedirectUri);

        McpOAuthTokenResponse exchanged;
        using (var scope = services.CreateScope())
            exchanged = await scope.ServiceProvider.GetRequiredService<IMcpOAuthService>()
                .ExchangeAuthorizationCodeAsync(code, RedirectUri);

        exchanged.UserId.Should().Be(_userA.ToString());
        using (var scope = services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<IMcpAuthorizationCodeStore>().ConsumeUnscopedAsync(code, RedirectUri))
                .Should().BeNull("a code is redeemed once");

        McpOAuthTokenResponse refreshed;
        using (var scope = services.CreateScope())
            refreshed = await scope.ServiceProvider.GetRequiredService<IMcpOAuthService>().RefreshAsync(exchanged.RefreshToken);

        using (var scope = services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IMcpOAuthService>().RevokeAsync(refreshed.RefreshToken);

        (await AllRefreshTokensAsync()).Should().HaveCount(2).And.OnlyContain(t => t.IsRevoked);
    }

    [DockerRequiredFact]
    public async Task Bulk_revoke_revokes_the_named_users_tokens_whoever_is_acting()
    {
        await IssueRefreshTokenAsync(_userA);
        await IssueRefreshTokenAsync(_userA);
        await IssueRefreshTokenAsync(_userB);

        // Acting as B, the way a person managing access revokes someone else's sessions.
        await using (var asB = CreateContext(_userB))
            await new RefreshTokenService(asB).RevokeAsync(_userA.ToString());

        var rows = await AllRefreshTokensAsync();
        rows.Where(t => t.UserId == _userA.ToString()).Should().HaveCount(2).And.OnlyContain(t => t.IsRevoked);
        rows.Single(t => t.UserId == _userB.ToString()).IsRevoked.Should().BeFalse("another person's token is untouched");

        await using (var asNoOne = CreateContext())
            await new RefreshTokenService(asNoOne).RevokeAsync(_userB.ToString());

        (await AllRefreshTokensAsync()).Should().OnlyContain(t => t.IsRevoked, "a revoke with no person in scope still finds them");
    }

    [DockerRequiredFact]
    public async Task Identity_tables_stay_readable_with_no_person_and_across_people()
    {
        await CreateUserAsync(_userA, "identity-a@test.com");
        await CreateUserAsync(_userB, "identity-b@test.com");
        await using (var services = CreateServices())
        using (var scope = services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var b = (await users.FindByIdAsync(_userB.ToString()))!;
            (await users.AddClaimAsync(b, new Claim(Permissions.ClaimType, Permissions.AiUse))).Succeeded.Should().BeTrue();
        }

        await using var asA = CreateContext(_userA);
        (await asA.Users.CountAsync()).Should().Be(2);
        (await asA.UserClaims.AnyAsync(c => c.UserId == _userB.ToString())).Should().BeTrue();

        await using var asNoOne = CreateContext();
        (await asNoOne.Users.CountAsync()).Should().Be(2);
    }
}
