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
    public void Never_puts_body_values_in_the_violation()
    {
        var violation = Check(HealthOperations.Get.Id, 200, """{"status":"ok","version":"0.0.0","secret":"hunter2"}""");

        Assert.NotNull(violation);
        Assert.DoesNotContain("hunter2", violation, StringComparison.Ordinal);
    }
}
