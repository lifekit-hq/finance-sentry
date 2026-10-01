namespace FinanceSentry.Tests.Integration.Agent;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Agent.Domain;
using FinanceSentry.Modules.Agent.Infrastructure;
using FinanceSentry.Modules.Agent.Infrastructure.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// The Owner query filter on <see cref="AgentDbContext"/>: a context acting for one person sees only that
/// person's conversations and the messages inside them, and a context with no person in scope sees none.
/// The module runs no job, so every read is a request acting for the signed-in person.
/// Real Postgres, matching the other filter suites.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AgentOwnerQueryFilterTests : IAsyncLifetime
{
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();
    private PostgreSqlContainer? _postgres;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _postgres.StartAsync();

        await using var setup = CreateContext();
        await setup.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    // Null acts as a background job: no person in scope.
    private AgentDbContext CreateContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<AgentDbContext>().UseNpgsql(_postgres!.GetConnectionString()).Options,
            new FixedCurrentUser(actingUser));

    private static Conversation NewConversation(Guid userId)
    {
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(), UserId = userId, ModelId = "test-model", CreatedAt = now, UpdatedAt = now,
        };
        conversation.Messages.Add(new Message
        {
            Id = Guid.NewGuid(), ConversationId = conversation.Id, Role = MessageRole.User, Content = "hi", CreatedAt = now,
        });
        return conversation;
    }

    private async Task SeedAsync(params object[] entities)
    {
        // Inserts are not filtered, so a no-person context writes any user's rows.
        await using var seed = CreateContext();
        seed.AddRange(entities);
        await seed.SaveChangesAsync();
    }

    [Fact]
    public void Every_per_user_entity_declares_the_Owner_filter()
    {
        using var ctx = CreateContext();

        var perUser = ctx.Model.GetEntityTypes().Where(e => e.FindProperty("UserId") is not null).ToList();

        perUser.Should().ContainSingle().Which.ClrType.Should().Be<Conversation>();
        ctx.Model.GetEntityTypes().Should().OnlyContain(
            e => e.GetDeclaredQueryFilters().Any(f => f.Key == OwnerQueryFilter.Name),
            "a message carries no UserId but is filtered through its conversation's owner");
    }

    [DockerRequiredFact]
    public async Task Each_person_sees_only_their_own_rows_and_no_person_sees_none()
    {
        var conversationA = NewConversation(_userA);
        var conversationB = NewConversation(_userB);
        await SeedAsync(conversationA, conversationB);

        await using (var asA = CreateContext(_userA))
        {
            (await asA.Conversations.Select(x => x.UserId).ToListAsync()).Should().Equal(_userA);
            (await asA.Messages.Select(x => x.ConversationId).ToListAsync()).Should().Equal(conversationA.Id);
        }

        await using (var asB = CreateContext(_userB))
        {
            (await asB.Conversations.AnyAsync(x => x.UserId == _userA)).Should().BeFalse(
                "the filter holds even when a query names another person explicitly");
            (await asB.Messages.AnyAsync(x => x.ConversationId == conversationA.Id)).Should().BeFalse(
                "a message is visible only to its conversation's owner");
        }

        await using var asNoOne = CreateContext();
        (await asNoOne.Conversations.AnyAsync()).Should().BeFalse("no person in scope matches no row");
        (await asNoOne.Messages.AnyAsync()).Should().BeFalse();
    }

    [DockerRequiredFact]
    public async Task Repository_reads_follow_the_acting_person()
    {
        var conversationA = NewConversation(_userA);
        await SeedAsync(conversationA, NewConversation(_userB));

        await using (var asA = CreateContext(_userA))
        {
            var repo = new ConversationRepository(asA);
            (await repo.ListAsync(_userA, default)).Should().ContainSingle();
            (await repo.GetWithMessagesAsync(_userA, conversationA.Id, default))!.Messages.Should().ContainSingle();
        }

        await using var asB = CreateContext(_userB);
        var asBRepo = new ConversationRepository(asB);
        (await asBRepo.ListAsync(_userA, default)).Should().BeEmpty(
            "naming another person does not lift the owner scope");
        (await asBRepo.GetWithMessagesAsync(_userA, conversationA.Id, default)).Should().BeNull();
        (await asBRepo.DeleteAsync(_userA, conversationA.Id, default)).Should().BeFalse();
    }
}
