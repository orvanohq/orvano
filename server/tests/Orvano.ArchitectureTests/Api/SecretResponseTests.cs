using System.Reflection;
using System.Text.RegularExpressions;

using Orvano.Modules.Platform;

using Shouldly;

using Xunit;

namespace Orvano.ArchitectureTests.Api;

// Spec 0006 SCR-4, spec 0017 AKY-3, AC-25: secrets are write only. Only the two endpoints that make a key answer with
// one, and no other response type has a field that could hold a secret.
public sealed partial class SecretResponseTests
{
    private static readonly string[] AllowedFields =
    [
        "Orvano.Modules.Platform.Console.CreatedApiKeyResponse.Key", // the one time show (UX-33)
        "Orvano.Modules.Platform.Console.SessionResponse.CsrfToken", // spec 0006 CON-2: the page must read it
    ];

    private static readonly Assembly Platform = typeof(PlatformModule).Assembly;

    [Fact]
    [Trait("Spec", "0006:SCR-4")]
    [Trait("Spec", "0017:AC-25")]
    public void No_response_type_has_a_secret_field_except_the_two_allowed_when_built() =>
        SecretFields(Platform.GetTypes()).ShouldBe(AllowedFields, ignoreOrder: true);

    [Fact]
    [Trait("Spec", "0006:SCR-4")]
    public void Secret_field_rule_fires_on_a_response_with_a_token_or_a_key() =>
        SecretFields([typeof(LeakyResponse), typeof(QuietResponse)]).ShouldBe(
        [
            $"{typeof(LeakyResponse).FullName}.ApiKey",
            $"{typeof(LeakyResponse).FullName}.PasswordHash",
            $"{typeof(LeakyResponse).FullName}.ResetToken",
        ],
        ignoreOrder: true);

    [Fact]
    [Trait("Spec", "0017:AKY-3")]
    [Trait("Spec", "0017:AC-25")]
    public void Only_key_create_and_regenerate_answer_with_a_full_key_when_built() =>
        HandlersReturning(Platform.GetTypes(), "CreatedApiKeyResponse").ShouldBe(["ApiKeyEndpoints.CreateAsync", "ApiKeyEndpoints.RegenerateAsync"]);

    private static string[] SecretFields(IEnumerable<Type> types) =>
    [
        .. types
            .Where(type => type.Name.EndsWith("Response", StringComparison.Ordinal))
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(property => SecretName().IsMatch(property.Name))
                .Select(property => $"{type.FullName}.{property.Name}")),
    ];

    private static string[] HandlersReturning(IEnumerable<Type> types, string response) =>
    [
        .. types
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => typeof(Task).IsAssignableFrom(method.ReturnType) && Mentions(method.ReturnType, response))
                .Select(method => $"{type.Name}.{method.Name}"))
            .Order(StringComparer.Ordinal),
    ];

    private static bool Mentions(Type type, string name) =>
        type.Name == name || type.GetGenericArguments().Any(argument => Mentions(argument, name));

    [GeneratedRegex("(Key|Secret|Password|Token|Hash)$", RegexOptions.CultureInvariant)]
    private static partial Regex SecretName();

    private sealed record LeakyResponse(string ApiKey, string PasswordHash, string ResetToken, string Prefix);

    private sealed record QuietResponse(string Prefix, string Last4);
}
