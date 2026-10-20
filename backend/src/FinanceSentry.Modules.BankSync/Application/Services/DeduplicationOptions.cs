namespace FinanceSentry.Modules.BankSync.Application.Services;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// The <c>Deduplication</c> configuration section. The master key is required: stored transaction hashes only
/// match under the key they were written with, so the host refuses to start without one.
/// </summary>
public sealed class DeduplicationOptions
{
    public const string SectionName = "Deduplication";

    /// <summary>Base64-encoded HMAC key for the transaction dedup hashes.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Deduplication:MasterKeyBase64 is required.")]
    public string MasterKeyBase64 { get; set; } = string.Empty;
}
