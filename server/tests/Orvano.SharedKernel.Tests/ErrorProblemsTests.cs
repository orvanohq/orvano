using System.Diagnostics;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Orvano.SharedKernel.Tests;

// Spec 0002 ERR-4: the one place an Error becomes ProblemDetails (interim shape until feature 10).
public sealed class ErrorProblemsTests
{
    public static TheoryData<ErrorKind, int> Statuses => new()
    {
        { ErrorKind.Validation, StatusCodes.Status400BadRequest },
        { ErrorKind.Unauthorized, StatusCodes.Status401Unauthorized },
        { ErrorKind.Forbidden, StatusCodes.Status403Forbidden },
        { ErrorKind.NotFound, StatusCodes.Status404NotFound },
        { ErrorKind.Conflict, StatusCodes.Status409Conflict },
        { ErrorKind.RateLimited, StatusCodes.Status429TooManyRequests },
        { ErrorKind.Unavailable, StatusCodes.Status503ServiceUnavailable },
    };

    [Theory]
    [Trait("Spec", "0002:ERR-4")]
    [MemberData(nameof(Statuses))]
    public void Status_follows_the_interim_kind_map_when_an_error_becomes_a_problem(ErrorKind kind, int status)
    {
        ErrorProblems.StatusFor(kind).ShouldBe(status);

        ErrorProblemResult problem = new Error("some_code", "Some message.", kind).ToProblem();

        problem.StatusCode.ShouldBe(status);
        problem.ProblemDetails.Status.ShouldBe(status);
    }

    [Fact]
    [Trait("Spec", "0002:ERR-4")]
    public void Every_error_kind_has_a_status_when_mapped() =>
        Enum.GetValues<ErrorKind>().ShouldAllBe(kind => ErrorProblems.StatusFor(kind) >= 400);

    [Fact]
    [Trait("Spec", "0002:ERR-4")]
    public void Status_throws_when_the_kind_is_not_defined() =>
        Should.Throw<ArgumentOutOfRangeException>(() => ErrorProblems.StatusFor((ErrorKind)99));

    [Fact]
    [Trait("Spec", "0002:ERR-4")]
    public void Problem_carries_the_code_and_the_message_as_detail_when_made()
    {
        ErrorProblemResult problem = Error.Conflict("name_taken", "That name is taken.").ToProblem();

        problem.ProblemDetails.Detail.ShouldBe("That name is taken.");
        problem.ProblemDetails.Extensions["code"].ShouldBe("name_taken");
    }

    [Fact]
    [Trait("Spec", "0002:ERR-4")]
    public void Problem_carries_the_current_trace_id_when_an_activity_is_running()
    {
        using Activity activity = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();

        ErrorProblemResult problem = Error.NotFound("not_found", "Not here.").ToProblem();

        problem.ProblemDetails.Extensions["traceId"].ShouldBe(activity.TraceId.ToString());
    }

    [Fact]
    [Trait("Spec", "0002:ERR-4")]
    public void Problem_has_no_trace_id_when_no_activity_is_running()
    {
        Activity? previous = Activity.Current;
        Activity.Current = null;
        try
        {
            Error.NotFound("not_found", "Not here.").ToProblem().ProblemDetails.Extensions.ShouldNotContainKey("traceId");
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    [Fact]
    [Trait("Spec", "0002:ERR-4")]
    public void Problem_lists_field_errors_under_errors_when_the_error_has_fields()
    {
        var fields = new Dictionary<string, string[]> { ["permissions"] = ["'read:x': not a valid grant."] };

        ErrorProblemResult problem = Error.Validation("validation_failed", "Some entries are not valid.", fields).ToProblem();

        problem.ProblemDetails.Extensions["errors"].ShouldBe(fields);
    }

    [Fact]
    [Trait("Spec", "0002:ERR-4")]
    public void Problem_has_no_errors_entry_when_the_fields_are_missing_or_empty()
    {
        Error.Validation("validation_failed", "Bad.").ToProblem().ProblemDetails.Extensions.ShouldNotContainKey("errors");
        Error.Validation("validation_failed", "Bad.", new Dictionary<string, string[]>())
            .ToProblem().ProblemDetails.Extensions.ShouldNotContainKey("errors");
    }

    [Fact]
    [Trait("Spec", "0002:ERR-4")]
    public void Problem_copies_the_errors_extensions_when_made()
    {
        ErrorProblemResult problem = Error.RateLimited("rate_limited", "Slow down.", retryAfterSeconds: 12).ToProblem();

        problem.StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
        problem.ProblemDetails.Extensions["retryAfterSeconds"].ShouldBe(12);
    }

    [Fact]
    [Trait("Spec", "0002:ERR-4")]
    public async Task Rate_limited_problem_sets_the_retry_after_header_when_written()
    {
        DefaultHttpContext http = await WriteAsync(Error.RateLimited("rate_limited", "Slow down.", retryAfterSeconds: 12));

        http.Response.StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
        http.Response.Headers.RetryAfter.ToString().ShouldBe("12");
    }

    [Fact]
    [Trait("Spec", "0002:ERR-4")]
    public async Task Rate_limited_problem_has_no_retry_after_header_when_the_error_has_no_wait()
    {
        var error = new Error("rate_limited", "Slow down.", ErrorKind.RateLimited);

        error.ToProblem().RetryAfterSeconds.ShouldBeNull();
        DefaultHttpContext http = await WriteAsync(error);

        http.Response.StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
        http.Response.Headers.ShouldNotContainKey("Retry-After");
    }

    [Fact]
    [Trait("Spec", "0002:ERR-4")]
    public async Task Problem_has_no_retry_after_header_when_the_error_is_not_rate_limited()
    {
        var extensions = new Dictionary<string, object?> { [ErrorProblems.RetryAfterSecondsExtension] = 5 };

        DefaultHttpContext http = await WriteAsync(new Error("busy", "Busy.", ErrorKind.Unavailable, Extensions: extensions));

        http.Response.StatusCode.ShouldBe(StatusCodes.Status503ServiceUnavailable);
        http.Response.Headers.ShouldNotContainKey("Retry-After");
    }

    [Fact]
    [Trait("Spec", "0002:ERR-4")]
    public void Problem_keeps_its_own_code_trace_id_and_errors_when_the_extensions_name_them()
    {
        var fields = new Dictionary<string, string[]> { ["name"] = ["Enter a name."] };
        var extensions = new Dictionary<string, object?> { ["code"] = "forged", ["traceId"] = "forged", ["errors"] = "forged", ["other"] = 1 };
        Activity? previous = Activity.Current;
        Activity.Current = null;
        try
        {
            IDictionary<string, object?> body = new Error("real_code", "Bad.", ErrorKind.Validation, fields, extensions)
                .ToProblem().ProblemDetails.Extensions;

            body["code"].ShouldBe("real_code");
            body["errors"].ShouldBe(fields);
            body.ShouldNotContainKey("traceId");
            body["other"].ShouldBe(1);
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    [Fact]
    [Trait("Spec", "0002:ERR-4")]
    public void Problem_throws_when_the_error_is_null() =>
        Should.Throw<ArgumentNullException>(() => ErrorProblems.ToProblem(null!));

    private static async Task<DefaultHttpContext> WriteAsync(Error error)
    {
        await using ServiceProvider services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Response.Body = new MemoryStream();

        await error.ToProblem().ExecuteAsync(http);
        return http;
    }
}
