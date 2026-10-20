namespace FinanceSentry.Modules.Companion.Domain;

using System.Globalization;

/// <summary>The numbers behind the Ledger correction log.</summary>
public static class LedgerCorrectionLimits
{
    public const int AgentSaidMaxLength = 2000;

    public const int WhatWasWrongMaxLength = 1000;

    public const int OwnerCorrectionMaxLength = 2000;

    public const int DefaultListLimit = 50;

    public const int MaxListLimit = 200;

    /// <summary>How far ahead of the server clock a reported correction time may be before it is refused.</summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>The calendar month a correction counts in, <c>yyyy-MM</c> in UTC.</summary>
    public static string MonthKey(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}
