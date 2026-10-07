using System.Text.Json.Serialization;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;

/// <summary>
/// What one in-page sign-in call returned, reduced to the fields the cabinet's own auth store reads. Holds the access
/// token on success, so it is never logged — <see cref="ToString"/> is redacted.
/// </summary>
public sealed class InzhurLoginStep
{
    [JsonPropertyName("stage")] public string? Stage { get; init; }
    [JsonPropertyName("status")] public int Status { get; init; }
    [JsonPropertyName("mode")] public string? Mode { get; init; }
    [JsonPropertyName("accessToken")] public string? AccessToken { get; init; }
    [JsonPropertyName("challengeId")] public string? ChallengeId { get; init; }
    [JsonPropertyName("message")] public string? Message { get; init; }
    [JsonPropertyName("attemptsLeft")] public int? AttemptsLeft { get; init; }

    public override string ToString() => $"InzhurLoginStep {{ Stage = {Stage}, Status = {Status}, Mode = {Mode}, Message = {Message} }}";
}

/// <summary>Where a sign-in stands after one step.</summary>
public abstract record InzhurLoginOutcome
{
    /// <summary>Signed in; the session is ready to store.</summary>
    public sealed record Authenticated(InzhurSession Session) : InzhurLoginOutcome
    {
        public override string ToString() => "Authenticated { Session = [redacted] }";
    }

    /// <summary>Inzhur sent an SMS; the code must arrive before <paramref name="CodeExpiresAt"/>.</summary>
    public sealed record CodeRequired(DateTime CodeExpiresAt) : InzhurLoginOutcome;

    /// <summary>The code was wrong; the same SMS may be retried while attempts remain.</summary>
    public sealed record InvalidCode(int? AttemptsLeft) : InzhurLoginOutcome;

    /// <summary>The sign-in ended; <paramref name="ErrorCode"/> is one of <see cref="InzhurErrorCodes"/>.</summary>
    public sealed record Failed(string ErrorCode, string Reason) : InzhurLoginOutcome;
}

/// <summary>Reads an in-page step the way the cabinet's own error handler does (design report §1; bundle error table).</summary>
public static class InzhurLoginStepInterpreter
{
    public enum Verdict { Authenticated, CodeRequired, InvalidCode, Failed }

    public sealed record Reading(Verdict Verdict, string? ErrorCode = null, int? AttemptsLeft = null);

    public static Reading Read(InzhurLoginStep step)
    {
        var ok = step.Status is >= 200 and < 300;
        if (ok && step.Mode == "authenticated" && !string.IsNullOrEmpty(step.AccessToken))
            return new Reading(Verdict.Authenticated);
        if (ok && step.Mode == "2fa_required" && !string.IsNullOrEmpty(step.ChallengeId) && step.Stage == "start")
            return new Reading(Verdict.CodeRequired);

        var message = step.Message ?? string.Empty;

        if (step.Status == 0 || step.Status == 429 || step.Status >= 500)
            return Fail(InzhurErrorCodes.LoginUnavailable);
        if (message.Contains("reCAPTCHA", StringComparison.OrdinalIgnoreCase))
            return Fail(InzhurErrorCodes.RecaptchaRejected);
        if (message.Contains("Invalid identifier or password", StringComparison.OrdinalIgnoreCase))
            return Fail(InzhurErrorCodes.InvalidCredentials);
        if (message.Equals("Invalid OTP", StringComparison.OrdinalIgnoreCase))
            return step.AttemptsLeft is null or > 0
                ? new Reading(Verdict.InvalidCode, AttemptsLeft: step.AttemptsLeft)
                : Fail(InzhurErrorCodes.TooManyAttempts);
        if (message.Contains("OTP no attempts", StringComparison.OrdinalIgnoreCase)
            || message.Contains("TOO_MANY_ATTEMPTS", StringComparison.OrdinalIgnoreCase))
            return Fail(InzhurErrorCodes.TooManyAttempts);
        if (message.Contains("2FA challenge", StringComparison.OrdinalIgnoreCase))
            return Fail(InzhurErrorCodes.ChallengeExpired);

        return Fail(InzhurErrorCodes.LoginFailed);
    }

    private static Reading Fail(string code) => new(Verdict.Failed, code);
}
