using System.Text.Json.Nodes;

namespace Orvano.SdkGen.Tests;

// Spec 0013 AC-40: the MFA and passkey error codes are in contract/errors.tsp with their statuses, and in every
// generated error code list (the server's, and the .NET, JS, and Dart SDKs'), read from the committed files.
public class MfaErrorCodesTests
{
    private static readonly (string Code, int Status)[] Spec =
    [
        ("invalid_mfa_ticket", 401),
        ("invalid_mfa_code", 401),
        ("invalid_passkey", 401),
        ("invalid_passkey_challenge", 400),
        ("mfa_verification_required", 403),
        ("mfa_required", 403),
        ("mfa_already_enabled", 409),
        ("mfa_not_enabled", 409),
        ("email_not_verified", 409),
        ("totp_not_pending", 409),
        ("factor_not_enabled", 409),
        ("passkey_already_registered", 409),
        ("passkey_limit", 409),
        ("passkeys_exist", 409),
        ("passkey_not_found", 404),
    ];

    public static TheoryData<string, int> Codes => new(Spec);

    public static TheoryData<string> Names => new(Spec.Select(e => e.Code));

    private static readonly string[] GeneratedLists =
    [
        "server/src/Orvano.Contract/Generated/ErrorCodes.cs",
        "sdks/dotnet/src/Orvano/Generated/ErrorCodes.cs",
        "sdks/js/src/generated/errors.ts",
        "sdks/dart/core/lib/src/generated/errors.dart",
    ];

    private static string Read(string relative) => File.ReadAllText(Path.Combine(Repo.Root, relative));

    [Theory]
    [MemberData(nameof(Codes))]
    public void The_contract_lists_the_code_with_its_status(string code, int status) // covers: AC-40
    {
        var catalog = JsonNode.Parse(Read("contract/dist/errors.json"))!.AsArray();

        var entry = Assert.Single(catalog, e => e!["code"]!.GetValue<string>() == code);

        Assert.Equal(status, entry!["status"]!.GetValue<int>());
        Assert.Matches($@"\b{code}\b", Read("contract/errors.tsp"));
        Assert.Contains(code, Repo.OpenApi()["components"]!["schemas"]!["ErrorCode"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>()));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Every_generated_error_code_list_has_the_code(string code) // covers: AC-40
    {
        foreach (var list in GeneratedLists)
            Assert.True(Read(list).Contains($"'{code}'", StringComparison.Ordinal) || Read(list).Contains($"\"{code}\"", StringComparison.Ordinal), $"{list} lacks {code}");
    }
}
