namespace FinanceSentry.Infrastructure.Encryption;

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

/// <summary>
/// Marks a value converter as field encryption, so the rotation target finds every encrypted column of a context from
/// its model - a module registers one rotation target for the context, not one per column.
/// </summary>
public interface IEncryptedFieldConverter;

/// <summary>Encrypts a <see cref="string"/> property into one text column (see <see cref="EncryptedField"/>).</summary>
public sealed class EncryptedStringConverter(ICredentialEncryptionService encryption)
    : ValueConverter<string, string>(
        plain => EncryptedField.Seal(encryption, plain),
        stored => EncryptedField.Open(encryption, stored)),
      IEncryptedFieldConverter;

/// <summary>
/// Encrypts a <see cref="decimal"/> property into one text column. The plaintext is the invariant-culture text of the
/// number, which keeps its scale, so a value reads back exactly as it was written.
/// </summary>
public sealed class EncryptedDecimalConverter(ICredentialEncryptionService encryption)
    : ValueConverter<decimal, string>(
        plain => EncryptedField.Seal(encryption, plain.ToString(CultureInfo.InvariantCulture)),
        stored => decimal.Parse(EncryptedField.Open(encryption, stored), NumberStyles.Number, CultureInfo.InvariantCulture)),
      IEncryptedFieldConverter;

public static class EncryptedFieldPropertyExtensions
{
    /// <summary>
    /// Stores the property field-encrypted in one text column. The encryption service the model is built with is the
    /// process's single credential service: EF caches the model per context type, so the converter binds it once.
    /// </summary>
    public static PropertyBuilder<string> IsEncrypted(this PropertyBuilder<string> property, ICredentialEncryptionService encryption)
        => property.HasConversion(new EncryptedStringConverter(encryption)).HasColumnType("text");

    /// <summary>An optional text property: a null stays null in the column, a value is encrypted.</summary>
    public static PropertyBuilder<string?> IsEncryptedOptional(this PropertyBuilder<string?> property, ICredentialEncryptionService encryption)
        // EF never runs a converter on a null (no ConvertsNulls), so the non-null string converter is safe for a nullable
        // property; the null-forgiving operator only bridges the converter's type argument's nullability annotation.
        => property.HasConversion(new EncryptedStringConverter(encryption)!).HasColumnType("text");

    public static PropertyBuilder<decimal> IsEncrypted(this PropertyBuilder<decimal> property, ICredentialEncryptionService encryption)
        => property.HasConversion(new EncryptedDecimalConverter(encryption)).HasColumnType("text");

    public static PropertyBuilder<decimal?> IsEncrypted(this PropertyBuilder<decimal?> property, ICredentialEncryptionService encryption)
        => property.HasConversion(new EncryptedDecimalConverter(encryption)).HasColumnType("text");
}
