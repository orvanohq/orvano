using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

using Orvano.Modules.Auth;
using Orvano.Modules.Platform;
using Orvano.SharedKernel;
using Orvano.SharedKernel.Api;

using Shouldly;

using Xunit;

namespace Orvano.ArchitectureTests.Api;

// Spec 0014 JSW-7, JSW-9, and PRB-5: module request bodies reject unknown members, every validation attribute's message
// is its field code (.NET 10 validation reports only messages, not the failing attribute), and an Optional<T> field is
// constrained only through OptionalValue<T>.
public sealed partial class WireRulesTests
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private static readonly Assembly[] Modules = [typeof(AuthModule).Assembly, typeof(PlatformModule).Assembly];

    private static readonly Assembly[] RequestAssemblies = [typeof(LogEventIds).Assembly, .. Modules];

    [Fact]
    [Trait("Spec", "0014:JSW-7")]
    public void Every_module_json_context_disallows_unmapped_members_when_built() =>
        ContextViolations(Modules.SelectMany(assembly => assembly.GetTypes())).ShouldBeEmpty();

    [Fact]
    [Trait("Spec", "0014:JSW-7")]
    public void Json_context_rule_fails_when_a_context_skips_unmapped_member_handling() =>
        ContextViolations([typeof(LenientContext), typeof(StrictContext)]).ShouldBe([typeof(LenientContext).FullName!]);

    [Fact]
    [Trait("Spec", "0014:PRB-5")]
    public void Every_validation_attribute_message_is_a_field_code_when_built() =>
        AttributeViolations(RequestAssemblies.SelectMany(assembly => assembly.GetTypes()).Where(type => !IsOptions(type))).ShouldBeEmpty();

    [Fact]
    [Trait("Spec", "0014:PRB-5")]
    public void Validation_attribute_rule_fails_when_a_message_is_missing_or_not_a_field_code() =>
        AttributeViolations([typeof(CodedRequest), typeof(UncodedRequest)]).ShouldBe(
        [
            $"{typeof(UncodedRequest).FullName}.Name: RequiredAttribute",
            $"{typeof(UncodedRequest).FullName}.Nickname: MaxLengthAttribute",
            $"{typeof(UncodedRequest).FullName}.ctor(Email): EmailAddressAttribute",
        ]);

    [Fact]
    [Trait("Spec", "0014:JSW-9"), Trait("Spec", "0014:PRB-5")]
    public void Every_optional_field_constraint_is_an_optional_value_attribute_when_built() =>
        OptionalViolations(RequestAssemblies.SelectMany(assembly => assembly.GetTypes())).ShouldBeEmpty();

    [Fact]
    [Trait("Spec", "0014:JSW-9"), Trait("Spec", "0014:PRB-5")]
    public void Optional_field_rule_fails_when_a_plain_attribute_is_on_an_optional_field_or_optional_value_on_another() =>
        OptionalViolations([typeof(OptionalRequest), typeof(MisusedOptionalRequest)]).ShouldBe(
        [
            $"{typeof(MisusedOptionalRequest).FullName}.Name: MaxLengthAttribute on Optional<T>",
            $"{typeof(MisusedOptionalRequest).FullName}.Nickname: OptionalValueAttribute`1 on String",
            $"{typeof(MisusedOptionalRequest).FullName}.ctor(Email): EmailAddressAttribute on Optional<T>",
        ]);

    private static IEnumerable<string> ContextViolations(IEnumerable<Type> types) =>
        from type in types
        where typeof(JsonSerializerContext).IsAssignableFrom(type) && !type.IsAbstract
        where type.GetCustomAttribute<JsonSourceGenerationOptionsAttribute>() is not { UnmappedMemberHandling: JsonUnmappedMemberHandling.Disallow }
        select type.FullName;

    // Settings classes (spec 0002 CFG): startup reads their messages, and no request ever binds them.
    private static bool IsOptions(Type type) => type.GetField("Section", BindingFlags.Public | BindingFlags.Static) is { IsLiteral: true };

    private static IEnumerable<string> AttributeViolations(IEnumerable<Type> types)
    {
        foreach (Type type in types)
        {
            foreach (PropertyInfo property in type.GetProperties(Declared))
            {
                foreach (ValidationAttribute attribute in property.GetCustomAttributes<ValidationAttribute>().Where(Uncoded))
                {
                    yield return $"{type.FullName}.{property.Name}: {attribute.GetType().Name}";
                }
            }

            foreach (ParameterInfo parameter in type.GetConstructors(Declared).SelectMany(ctor => ctor.GetParameters()))
            {
                foreach (ValidationAttribute attribute in parameter.GetCustomAttributes<ValidationAttribute>().Where(Uncoded))
                {
                    yield return $"{type.FullName}.ctor({parameter.Name}): {attribute.GetType().Name}";
                }
            }
        }
    }

    // .NET validation checks an attribute against the property's own value, so on an Optional<T> it sees the struct and
    // refuses every request; OptionalValue<T> unwraps it, and means nothing on any other type.
    private static IEnumerable<string> OptionalViolations(IEnumerable<Type> types)
    {
        foreach (Type type in types)
        {
            IEnumerable<(string Name, Type Type, IEnumerable<ValidationAttribute> Attributes)> members =
            [
                .. type.GetProperties(Declared).Select(property => (property.Name, property.PropertyType, property.GetCustomAttributes<ValidationAttribute>())),
                .. type.GetConstructors(Declared).SelectMany(ctor => ctor.GetParameters())
                    .Select(parameter => ($"ctor({parameter.Name})", parameter.ParameterType, parameter.GetCustomAttributes<ValidationAttribute>())),
            ];
            foreach ((string name, Type memberType, IEnumerable<ValidationAttribute> attributes) in members)
            {
                bool optional = memberType.IsGenericType && memberType.GetGenericTypeDefinition() == typeof(Optional<>);
                foreach (ValidationAttribute attribute in attributes)
                {
                    bool unwraps = attribute.GetType().IsGenericType && attribute.GetType().GetGenericTypeDefinition() == typeof(OptionalValueAttribute<>);
                    if (optional != unwraps)
                    {
                        yield return $"{type.FullName}.{name}: {attribute.GetType().Name} on {(optional ? "Optional<T>" : memberType.Name)}";
                    }
                }
            }
        }
    }

    private static bool Uncoded(ValidationAttribute attribute) => attribute.ErrorMessage is not { } message || !FieldCodes.All.Contains(message);

    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
    [JsonSerializable(typeof(CodedRequest))]
    private sealed partial class LenientContext : JsonSerializerContext;

    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
    [JsonSerializable(typeof(CodedRequest))]
    private sealed partial class StrictContext : JsonSerializerContext;

    private sealed record CodedRequest(
        [property: Required(ErrorMessage = FieldCodes.Required)] string? Name,
        [property: MaxLength(5, ErrorMessage = FieldCodes.TooLong)] string? Nickname);

    private sealed record OptionalRequest(
        [property: OptionalValue<MaxLengthAttribute>(5, ErrorMessage = FieldCodes.TooLong)] Optional<string> Name,
        [property: MaxLength(5, ErrorMessage = FieldCodes.TooLong)] string? Nickname);

    private sealed record MisusedOptionalRequest(
        [property: MaxLength(5, ErrorMessage = FieldCodes.TooLong)] Optional<string> Name,
        [property: OptionalValue<MaxLengthAttribute>(5, ErrorMessage = FieldCodes.TooLong)] string? Nickname,
        [EmailAddress(ErrorMessage = FieldCodes.InvalidEmail)] Optional<string?> Email);

    private sealed record UncodedRequest([EmailAddress] string? Email)
    {
        [Required]
        public string? Name { get; init; }

        [MaxLength(5, ErrorMessage = "Too long.")]
        public string? Nickname { get; init; }
    }
}
