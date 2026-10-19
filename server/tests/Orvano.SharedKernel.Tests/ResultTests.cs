using Shouldly;

using Xunit;

namespace Orvano.SharedKernel.Tests;

// Spec 0002 ERR-1 and ERR-2: a value or an expected failure, never a silent success.
public sealed class ResultTests
{
    private static readonly Error Missing = Error.NotFound("not_found", "The resource was not found.");

    [Fact]
    [Trait("Spec", "0002:ERR-1")]
    public void Result_is_a_success_holding_the_value_when_made_from_a_value()
    {
        Result<int> result = 42;

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
        result.Error.ShouldBeNull();
    }

    [Fact]
    [Trait("Spec", "0002:ERR-1")]
    public void Result_is_a_failure_holding_the_error_when_made_from_an_error()
    {
        Result<string> result = Missing;

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(Missing);
        result.Value.ShouldBeNull();
    }

    [Fact]
    [Trait("Spec", "0002:ERR-1")]
    public void Default_result_is_never_a_success_when_nothing_set_it()
    {
        default(Result<int>).IsSuccess.ShouldBeFalse();
        default(Result).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    [Trait("Spec", "0002:ERR-1")]
    public void Result_throws_when_made_from_a_null_error()
    {
        Should.Throw<ArgumentNullException>(() =>
        {
            Result<int> result = (Error)null!;
            return result;
        });
        Should.Throw<ArgumentNullException>(() =>
        {
            Result result = (Error)null!;
            return result;
        });
    }

    [Fact]
    [Trait("Spec", "0002:ERR-1")]
    public void Command_result_is_a_success_with_no_error_when_it_is_Success()
    {
        Result.Success.IsSuccess.ShouldBeTrue();
        Result.Success.Error.ShouldBeNull();
    }

    [Fact]
    [Trait("Spec", "0002:ERR-1")]
    public void Command_result_is_a_failure_holding_the_error_when_made_from_an_error()
    {
        Result result = Missing;

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(Missing);
    }

    [Fact]
    [Trait("Spec", "0002:ERR-1")]
    public void Error_factories_set_the_kind_code_and_message_when_called()
    {
        var fields = new Dictionary<string, string[]> { ["email"] = ["Enter an email address."] };

        Error.Validation("validation_failed", "Bad input.", fields).ShouldBe(new Error("validation_failed", "Bad input.", ErrorKind.Validation, fields));
        Error.Unauthorized("unauthorized", "No.").Kind.ShouldBe(ErrorKind.Unauthorized);
        Error.Forbidden("forbidden", "No.").Kind.ShouldBe(ErrorKind.Forbidden);
        Error.NotFound("not_found", "No.").Kind.ShouldBe(ErrorKind.NotFound);
        Error.Conflict("conflict", "No.").Kind.ShouldBe(ErrorKind.Conflict);
        Error.Unavailable("timeout", "No.").Kind.ShouldBe(ErrorKind.Unavailable);
    }

    [Fact]
    [Trait("Spec", "0002:ERR-4")]
    public void Rate_limited_error_carries_retry_after_seconds_when_made()
    {
        var error = Error.RateLimited("rate_limited", "Slow down.", retryAfterSeconds: 30);

        error.Kind.ShouldBe(ErrorKind.RateLimited);
        error.Extensions.ShouldNotBeNull()["retryAfterSeconds"].ShouldBe(30);
    }

    [Fact]
    [Trait("Spec", "0002:ERR-1")]
    public void Default_result_carries_the_stable_default_error_when_nothing_set_it()
    {
        Result<int> value = default;
        Result command = default;

        value.Error.ShouldBe(DefaultResult.Error);
        command.Error.ShouldBe(DefaultResult.Error);
        DefaultResult.Error.Code.ShouldBe("default_result");
        DefaultResult.Error.Kind.ShouldBe(ErrorKind.Unavailable);
    }
}
