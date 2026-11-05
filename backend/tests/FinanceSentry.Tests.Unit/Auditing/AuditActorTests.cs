namespace FinanceSentry.Tests.Unit.Auditing;

using FinanceSentry.Core.Auditing;
using FinanceSentry.Core.Auth;
using FluentAssertions;
using Xunit;

public sealed class AuditActorTests
{
    private static AuditActor ActorFor(Guid? person, AuditActorKind defaultKind = AuditActorKind.OwnerSession)
        => new(new FixedUser(person), () => defaultKind);

    [Fact]
    public void WithAPerson_TheActorIsTheHostsDefaultKind()
    {
        var actor = ActorFor(Guid.NewGuid(), AuditActorKind.McpService);

        actor.Kind.Should().Be(AuditActorKind.McpService);
    }

    [Fact]
    public void WithNoPerson_TheActorIsAJob_WhateverTheDefault()
    {
        var actor = ActorFor(person: null);

        actor.Kind.Should().Be(AuditActorKind.Job);
        actor.UserId.Should().BeNull();
    }

    [Fact]
    public void Use_ActsAsTheGivenKindUntilDisposed()
    {
        var actor = ActorFor(Guid.NewGuid());

        using (actor.Use(AuditActorKind.InAppAgent))
            actor.Kind.Should().Be(AuditActorKind.InAppAgent);

        actor.Kind.Should().Be(AuditActorKind.OwnerSession);
    }

    [Fact]
    public void Use_Nested_RestoresThePreviousKind()
    {
        var actor = ActorFor(Guid.NewGuid());

        using (actor.Use(AuditActorKind.InAppAgent))
        {
            using (actor.Use(AuditActorKind.McpPersonal))
                actor.Kind.Should().Be(AuditActorKind.McpPersonal);

            actor.Kind.Should().Be(AuditActorKind.InAppAgent);
        }
    }

    private sealed class FixedUser(Guid? userId) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
    }
}
