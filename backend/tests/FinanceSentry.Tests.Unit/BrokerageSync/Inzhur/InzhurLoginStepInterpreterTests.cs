using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;
using FluentAssertions;
using Xunit;
using Verdict = FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur.InzhurLoginStepInterpreter.Verdict;

namespace FinanceSentry.Tests.Unit.BrokerageSync.Inzhur;

public class InzhurLoginStepInterpreterTests
{
    private static InzhurLoginStepInterpreter.Reading Read(
        int status, string stage = "start", string? mode = null, string? token = null, string? challenge = null, string? message = null, int? attemptsLeft = null)
        => InzhurLoginStepInterpreter.Read(new InzhurLoginStep
        {
            Stage = stage, Status = status, Mode = mode, AccessToken = token, ChallengeId = challenge, Message = message, AttemptsLeft = attemptsLeft,
        });

    [Fact]
    public void Authenticated_with_a_token_signs_in()
        => Read(200, mode: "authenticated", token: InzhurFakes.AccessToken).Verdict.Should().Be(Verdict.Authenticated);

    [Fact]
    public void Authenticated_without_a_token_is_a_failure()
        => Read(200, mode: "authenticated").ErrorCode.Should().Be(InzhurErrorCodes.LoginFailed);

    [Fact]
    public void Two_factor_challenge_on_start_waits_for_the_code()
        => Read(200, mode: "2fa_required", challenge: "fake-challenge").Verdict.Should().Be(Verdict.CodeRequired);

    [Fact]
    public void Two_factor_challenge_after_a_code_is_not_another_wait()
        => Read(200, stage: "verify", mode: "2fa_required", challenge: "fake-challenge").Verdict.Should().Be(Verdict.Failed);

    [Theory]
    [InlineData(0)]
    [InlineData(429)]
    [InlineData(502)]
    public void No_answer_busy_or_down_is_unavailable(int status)
        => Read(status).ErrorCode.Should().Be(InzhurErrorCodes.LoginUnavailable);

    [Theory]
    [InlineData("reCAPTCHA verification failed", InzhurErrorCodes.RecaptchaRejected)]
    [InlineData("Invalid identifier or password", InzhurErrorCodes.InvalidCredentials)]
    [InlineData("OTP no attempts left", InzhurErrorCodes.TooManyAttempts)]
    [InlineData("TOO_MANY_ATTEMPTS", InzhurErrorCodes.TooManyAttempts)]
    [InlineData("2FA challenge not found or expired", InzhurErrorCodes.ChallengeExpired)]
    [InlineData("Something else", InzhurErrorCodes.LoginFailed)]
    public void Inzhur_error_texts_map_to_the_matching_code(string message, string expected)
        => Read(400, message: message).ErrorCode.Should().Be(expected);

    [Fact]
    public void Wrong_code_with_attempts_left_may_be_retried()
    {
        var reading = Read(400, stage: "verify", message: "Invalid OTP", attemptsLeft: 2);

        reading.Verdict.Should().Be(Verdict.InvalidCode);
        reading.AttemptsLeft.Should().Be(2);
    }

    [Fact]
    public void Wrong_code_with_no_attempts_left_ends_the_sign_in()
        => Read(400, stage: "verify", message: "Invalid OTP", attemptsLeft: 0).ErrorCode.Should().Be(InzhurErrorCodes.TooManyAttempts);

    [Fact]
    public void A_step_never_prints_its_access_token()
        => new InzhurLoginStep { Status = 200, AccessToken = InzhurFakes.AccessToken }.ToString().Should().NotContain(InzhurFakes.AccessToken);
}
