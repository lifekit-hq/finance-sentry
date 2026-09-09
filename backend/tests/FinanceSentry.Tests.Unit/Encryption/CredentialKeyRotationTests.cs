namespace FinanceSentry.Tests.Unit.Encryption;

using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Encryption;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// The v1 → v2 rotation path, end to end over a real <see cref="BankSyncDbContext"/> (issue #493).
///
/// Removing the disclosed fallback stops NEW credentials being written under a published key; it
/// does nothing for the rows already written. Rotation is what actually closes the disclosure, so
/// it is exercised through the same objects production uses — the entity, the context, and the real
/// AES-GCM service with two configured key versions — rather than through a stub that could pass
/// while the real decrypt-old/encrypt-new path is broken.
/// </summary>
public class CredentialKeyRotationTests
{
    // Two distinct 32-byte keys. v1 stands in for the disclosed key, v2 for its replacement.
    private const string KeyV1Base64 = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private const string KeyV2Base64 = "ZmVkY2JhOTg3NjU0MzIxMGZlZGNiYTk4NzY1NDMyMTA=";

    private static readonly Dictionary<int, string> BothKeys = new()
    {
        [1] = KeyV1Base64,
        [2] = KeyV2Base64,
    };

    private static CredentialEncryptionService ServiceAtVersion(int currentVersion) =>
        new(Options.Create(new EncryptionOptions
        {
            CurrentKeyVersion = currentVersion,
            Keys = new Dictionary<int, string>(BothKeys),
        }));

    private static BankSyncDbContext NewContext() =>
        new(new DbContextOptionsBuilder<BankSyncDbContext>()
            .UseInMemoryDatabase($"rotation-{Guid.NewGuid()}")
            .Options);

    [Fact]
    public async Task RotateAsync_ReencryptsV1RowsUnderV2_AndPlaintextSurvives()
    {
        const string token = "monobank-access-token-that-must-survive-rotation";

        await using var db = NewContext();

        // Written the way production wrote it before the new key existed: key version 1.
        var underV1 = ServiceAtVersion(1).Encrypt(token);
        underV1.KeyVersion.Should().Be(1);
        db.MonobankCredentials.Add(
            new MonobankCredential(Guid.NewGuid(), underV1.Ciphertext, underV1.Iv, underV1.AuthTag));
        await db.SaveChangesAsync();

        var atV2 = ServiceAtVersion(2);
        var rotated = await new MonobankCredentialRotationTarget(db, atV2).RotateAsync(2, default);

        rotated.Should().Be(1);

        var row = await db.MonobankCredentials.SingleAsync();
        row.KeyVersion.Should().Be(2, "the payload and the version move together");
        atV2.Decrypt(row.EncryptedToken, row.Iv, row.AuthTag, row.KeyVersion)
            .Should().Be(token, "rotation re-encrypts the same secret, it never rewrites it");
    }

    [Fact]
    public async Task RotateAsync_IsIdempotent_ASecondRunMigratesNothing()
    {
        // Rotation runs on every startup, so a no-op second run is not a nicety: without it every
        // boot would rewrite every credential row.
        await using var db = NewContext();

        var underV1 = ServiceAtVersion(1).Encrypt("token");
        db.MonobankCredentials.Add(
            new MonobankCredential(Guid.NewGuid(), underV1.Ciphertext, underV1.Iv, underV1.AuthTag));
        await db.SaveChangesAsync();

        var atV2 = ServiceAtVersion(2);
        var target = new MonobankCredentialRotationTarget(db, atV2);

        (await target.RotateAsync(2, default)).Should().Be(1);
        (await target.RotateAsync(2, default)).Should().Be(0);
    }

    [Fact]
    public async Task RotateAsync_LeavesAConnectionThatHoldsNoTokenAlone()
    {
        // A TrueLayer connection exists between consent and finalize with no refresh token stored.
        // It holds no secret, so there is nothing to rotate — and decrypting its empty payload
        // would throw, taking the whole startup rotation down with it.
        await using var db = NewContext();

        db.TrueLayerConnections.Add(
            new TrueLayerConnection(Guid.NewGuid(), "mock-bank", "Mock Bank", "ref-123"));
        await db.SaveChangesAsync();

        var rotated = await new TrueLayerConnectionRotationTarget(db, ServiceAtVersion(2))
            .RotateAsync(2, default);

        rotated.Should().Be(0);
    }
}
