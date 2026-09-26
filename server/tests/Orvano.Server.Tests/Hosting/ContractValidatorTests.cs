using System.Text;
using Orvano.Contract;
using Orvano.Server.Hosting;

namespace Orvano.Server.Tests.Hosting;

// Spec 0001, AC-9: in the Test environment every response is checked against openapi.json.
public class ContractValidatorTests
{
    private static readonly ContractValidator Validator = Load();

    private static ContractValidator Load()
    {
        using var json = ContractDocument.OpenJson();
        return ContractValidator.Load(json);
    }

    private static string? Check(string? operationId, int status, string body, string? contentType = "application/json; charset=utf-8") =>
        Validator.Check(operationId, status, contentType, Encoding.UTF8.GetBytes(body));

    [Fact]
    public void Accepts_a_response_that_matches_the_contract() =>
        Assert.Null(Check(HealthOperations.Get.Id, 200, """{"status":"ok","version":"0.0.0"}"""));

    [Fact]
    public void Rejects_a_field_the_contract_does_not_have()
    {
        var violation = Check(HealthOperations.Get.Id, 200, """{"status":"ok","version":"0.0.0","uptime":42}""");

        Assert.NotNull(violation);
        Assert.Contains("/uptime", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_missing_required_field() =>
        Assert.NotNull(Check(HealthOperations.Get.Id, 200, """{"status":"ok"}"""));

    [Fact]
    public void Rejects_a_wrong_type() =>
        Assert.NotNull(Check(HealthOperations.Get.Id, 200, """{"status":"ok","version":1}"""));

    [Fact]
    public void Rejects_an_endpoint_the_contract_does_not_declare() =>
        Assert.NotNull(Check("health.secret", 200, "{}"));

    [Fact]
    public void Rejects_an_undeclared_success_status() =>
        Assert.NotNull(Check(HealthOperations.Get.Id, 201, """{"status":"ok","version":"0.0.0"}"""));

    [Fact]
    public void Accepts_a_problem_for_an_error_status()
    {
        const string problem = """{"type":"https://orvano.dev/errors/not_found","title":"Not Found","status":404,"code":"not_found","requestId":"abc"}""";

        Assert.Null(Check(HealthOperations.Get.Id, 404, problem, "application/problem+json"));
    }

    [Fact]
    public void Rejects_an_error_body_that_is_not_problem_json() =>
        Assert.NotNull(Check(HealthOperations.Get.Id, 500, """{"type":"t","title":"t","status":500,"code":"c","requestId":"r"}"""));

    [Fact]
    public void Rejects_a_problem_with_a_member_the_contract_lacks()
    {
        const string problem = """{"type":"t","title":"t","status":500,"code":"c","requestId":"r","traceId":"x"}""";

        var violation = Check(HealthOperations.Get.Id, 500, problem, "application/problem+json");

        Assert.NotNull(violation);
        Assert.Contains("/traceId", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_problem_without_a_code() =>
        Assert.NotNull(Check(HealthOperations.Get.Id, 400, """{"type":"t","title":"t","status":400,"requestId":"r"}""", "application/problem+json"));

    [Fact]
    public void Rejects_a_body_on_a_no_content_response() =>
        Assert.NotNull(Check(TestOperations.Conflict.Id, 204, "{}"));

    [Fact]
    public void Never_puts_body_values_in_the_violation()
    {
        var violation = Check(HealthOperations.Get.Id, 200, """{"status":"ok","version":"0.0.0","secret":"hunter2"}""");

        Assert.NotNull(violation);
        Assert.DoesNotContain("hunter2", violation, StringComparison.Ordinal);
    }
}
