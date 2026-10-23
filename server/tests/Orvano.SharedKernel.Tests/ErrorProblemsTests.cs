using System.Diagnostics;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using Orvano.SharedKernel.Api;

using Shouldly;

using Xunit;

namespace Orvano.SharedKernel.Tests;

// Spec 0002 ERR-4 and spec 0014 PRB-1, PRB-2, PRB-6, KND-1: the one place an Error becomes a problem.
public sealed class ErrorProblemsTests
{
    public static TheoryData<ErrorKind, int> Statuses => new()
    {
        { ErrorKind.Validation, StatusCodes.Status400BadRequest },
        { ErrorKind.Unauthorized, StatusCodes.Status401Unauthorized },
        { ErrorKind.Forbidden, StatusCodes.Status403Forbidden },
        { ErrorKind.NotFound, StatusCodes.Status404NotFound },
        { ErrorKind.Conflict, StatusCodes.Status409Conflict },
        { ErrorKind.PreconditionFailed, StatusCodes.Status412PreconditionFailed },
        { ErrorKind.RateLimited, StatusCodes.Status429TooManyRequests },
        { ErrorKind.Unavailable, StatusCodes.Status503ServiceUnavailable },
    };

    [Theory]
    [Trait("Spec", "0014:KND-1")]
    [MemberData(nameof(Statuses))]
    public void Status_follows_the_kind_table_when_an_error_becomes_a_problem(ErrorKind kind, int status)
    {
        ErrorProblems.StatusFor(kind).ShouldBe(status);

        ErrorProblemResult problem = new Error("some_code", "Some message.", kind).ToProblem();

        problem.StatusCode.ShouldBe(status);
        problem.ProblemDetails.Status.ShouldBe(status);
    }

    [Fact]
    [Trait("Spec", "0014:KND-1")]
    public void Every_error_kind_has_a_status_when_mapped() =>
        Enum.GetValues<ErrorKind>().ShouldAllBe(kind => ErrorProblems.StatusFor(kind) >= 400);

    [Fact]
    [Trait("Spec", "0002:ERR-4")]
    public void Status_throws_when_the_kind_is_not_defined() =>
        Should.Throw<ArgumentOutOfRangeException>(() => ErrorProblems.StatusFor((ErrorKind)99));

    [Fact]
    [Trait("Spec", "0014:PRB-1")]
    public void Problem_has_type_about_blank_the_reason_phrase_as_title_and_the_message_as_detail_when_made()
    {
        ErrorProblemResult problem = Error.Conflict("name_taken", "That name is taken.").ToProblem();

        problem.ProblemDetails.Type.ShouldBe("about:blank");
        problem.ProblemDetails.Title.ShouldBe("Conflict");
        problem.ProblemDetails.Detail.ShouldBe("That name is taken.");
        problem.ProblemDetails.Extensions["code"].ShouldBe("name_taken");
    }

    [Fact]
    [Trait("Spec", "0014:PRB-1")]
    public void Problem_carries_the_current_trace_id_when_an_activity_is_running()
    {
        using Activity activity = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();

        ErrorProblemResult problem = Error.NotFound("not_found", "Not here.").ToProblem();

        problem.ProblemDetails.Extensions["traceId"].ShouldBe(activity.TraceId.ToString());
    }

    [Fact]
    [Trait("Spec", "0014:PRB-1")]
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
    [Trait("Spec", "0014:PRB-3")]
    public void Problem_lists_field_errors_under_errors_when_the_error_has_fields()
    {
        FieldError[] fields = [new("permissions", FieldLocation.Body, FieldCodes.InvalidValue, "'read:x': not a valid grant.")];

        ErrorProblemResult problem = Error.Validation("validation_failed", "Some entries are not valid.", fields).ToProblem();

        problem.ProblemDetails.Extensions["errors"].ShouldBe(fields);
    }

    [Fact]
    [Trait("Spec", "0014:PRB-3")]
    public void Problem_has_no_errors_entry_when_the_fields_are_missing_or_empty()
    {
        Error.Validation("validation_failed", "Bad.").ToProblem().ProblemDetails.Extensions.ShouldNotContainKey("errors");
        Error.Validation("validation_failed", "Bad.", []).ToProblem().ProblemDetails.Extensions.ShouldNotContainKey("errors");
    }

    [Fact]
    [Trait("Spec", "0014:PRB-1")]
    public void Problem_copies_the_errors_extensions_when_made()
    {
        var extensions = new Dictionary<string, object?> { ["currentVersion"] = "AAAAAAAAAAE" };

        ErrorProblemResult problem = Error.PreconditionFailed("version_conflict", "Stale.", extensions).ToProblem();

        problem.StatusCode.ShouldBe(StatusCodes.Status412PreconditionFailed);
        problem.ProblemDetails.Extensions["currentVersion"].ShouldBe("AAAAAAAAAAE");
    }

    [Theory]
    [Trait("Spec", "0014:PRB-2"), Trait("Spec", "0014:PRB-6")]
    [InlineData(12.0, 12)]
    [InlineData(0.2, 1)]
    [InlineData(0.0, 0)]
    public async Task Rate_limited_problem_sets_retry_after_in_the_body_and_the_header_in_whole_seconds(double wait, int seconds)
    {
        var error = Error.RateLimited("rate_limited", "Slow down.", TimeSpan.FromSeconds(wait));

        error.ToProblem().ProblemDetails.Extensions["retryAfter"].ShouldBe(seconds);
        DefaultHttpContext http = await WriteAsync(error);

        http.Response.StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
        http.Response.Headers.RetryAfter.ToString().ShouldBe(seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    [Trait("Spec", "0014:PRB-6")]
    public async Task Unavailable_problem_sets_retry_after_when_it_knows_when_to_come_back()
    {
        DefaultHttpContext http = await WriteAsync(Error.Unavailable("unavailable", "Busy.", TimeSpan.FromSeconds(5)));

        http.Response.StatusCode.ShouldBe(StatusCodes.Status503ServiceUnavailable);
        http.Response.Headers.RetryAfter.ToString().ShouldBe("5");
    }

    [Fact]
    [Trait("Spec", "0014:PRB-6")]
    public async Task Problem_has_no_retry_after_when_the_error_has_no_wait()
    {
        var error = Error.Unavailable("unavailable", "Busy.");

        error.ToProblem().ProblemDetails.Extensions.ShouldNotContainKey("retryAfter");
        DefaultHttpContext http = await WriteAsync(error);

        http.Response.Headers.ShouldNotContainKey("Retry-After");
    }

    [Fact]
    [Trait("Spec", "0014:PRB-6")]
    public async Task Problem_has_no_retry_after_when_the_error_is_neither_rate_limited_nor_unavailable()
    {
        var error = new Error("conflict", "Busy.", ErrorKind.Conflict, RetryAfter: TimeSpan.FromSeconds(5));

        error.ToProblem().ProblemDetails.Extensions.ShouldNotContainKey("retryAfter");
        DefaultHttpContext http = await WriteAsync(error);

        http.Response.Headers.ShouldNotContainKey("Retry-After");
    }

    [Fact]
    [Trait("Spec", "0014:PRB-1")]
    public void Problem_keeps_its_own_members_when_the_extensions_name_them()
    {
        FieldError[] fields = [new("name", FieldLocation.Body, FieldCodes.Required, "Enter a name.")];
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = "forged",
            ["traceId"] = "forged",
            ["errors"] = "forged",
            ["retryAfter"] = 9,
            ["type"] = "forged",
            ["other"] = 1,
        };
        Activity? previous = Activity.Current;
        Activity.Current = null;
        try
        {
            ErrorProblemResult problem = new Error("real_code", "Bad.", ErrorKind.Validation, fields, extensions).ToProblem();
            IDictionary<string, object?> body = problem.ProblemDetails.Extensions;

            body["code"].ShouldBe("real_code");
            body["errors"].ShouldBe(fields);
            body.ShouldNotContainKey("traceId");
            body.ShouldNotContainKey("retryAfter");
            body.ShouldNotContainKey("type");
            body["other"].ShouldBe(1);
            problem.ProblemDetails.Type.ShouldBe("about:blank");
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
