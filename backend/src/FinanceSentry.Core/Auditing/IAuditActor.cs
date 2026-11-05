using FinanceSentry.Core.Auth;

namespace FinanceSentry.Core.Auditing;

/// <summary>
/// Who is acting in the current unit of work, for the audit trail. A host registers the default for its transport (the
/// API: the signed-in person; the MCP host: a personal or service token); the in-app agent switches it for the span of
/// one tool call with <see cref="Use"/>. With no person in scope the actor is always a <see cref="AuditActorKind.Job"/>.
/// </summary>
public interface IAuditActor
{
    AuditActorKind Kind { get; }

    /// <summary>The person acted as; null when no person is in scope.</summary>
    Guid? UserId { get; }

    /// <summary>Acts as <paramref name="kind"/> until the returned scope is disposed.</summary>
    IDisposable Use(AuditActorKind kind);
}

/// <inheritdoc />
public sealed class AuditActor(ICurrentUser currentUser, Func<AuditActorKind> defaultKind) : IAuditActor
{
    private AuditActorKind? _override;

    public Guid? UserId => currentUser.UserId;

    public AuditActorKind Kind => UserId is null ? AuditActorKind.Job : _override ?? defaultKind();

    public IDisposable Use(AuditActorKind kind)
    {
        var previous = _override;
        _override = kind;
        return new Restore(this, previous);
    }

    private sealed class Restore(AuditActor owner, AuditActorKind? previous) : IDisposable
    {
        public void Dispose() => owner._override = previous;
    }
}
