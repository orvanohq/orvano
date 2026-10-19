using System.Reflection;
using System.Reflection.Emit;

namespace Orvano.ArchitectureTests.Access;

/// <summary>
/// Every type a type touches: its signatures (base, interfaces, fields, properties, methods, attributes) and the
/// tokens in its method bodies (calls, field access, casts, <c>new</c>). Enough to prove an assembly area never
/// reaches a namespace, without an extra package.
/// </summary>
internal static class TypeReferences
{
    private const BindingFlags Everything =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    /// <summary>Lines naming each member of <paramref name="types"/> that touches a type whose full name starts with a banned prefix.</summary>
    public static IEnumerable<string> Violations(IEnumerable<Type> types, IReadOnlyList<string> bannedPrefixes) =>
        from type in types
        from referenced in Of(type).Distinct()
        where referenced.FullName is { } name && bannedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal))
        select $"{type.FullName} uses {referenced.FullName}";

    public static IEnumerable<Type> Of(Type type)
    {
        IEnumerable<Type> signature = new[] { type.BaseType }
            .Concat(type.GetInterfaces())
            .Concat(type.GetCustomAttributesData().Select(attribute => attribute.AttributeType))
            .Concat(type.GetFields(Everything).Select(field => field.FieldType))
            .Concat(type.GetProperties(Everything).Select(property => property.PropertyType))
            .Concat(type.GetEvents(Everything).Select(@event => @event.EventHandlerType))
            .OfType<Type>();

        IEnumerable<MethodBase> methods = type.GetMethods(Everything).Cast<MethodBase>().Concat(type.GetConstructors(Everything));
        foreach (MethodBase method in methods)
        {
            signature = signature.Concat(method.GetParameters().Select(parameter => parameter.ParameterType));
            if (method is MethodInfo info)
            {
                signature = signature.Append(info.ReturnType);
            }

            signature = signature.Concat(BodyReferences(method));
        }

        return signature.SelectMany(Expand);
    }

    // Generic arguments, arrays, and by ref types count as their parts.
    private static IEnumerable<Type> Expand(Type type)
    {
        if (type.HasElementType)
        {
            return Expand(type.GetElementType()!);
        }

        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            return type.GetGenericArguments().SelectMany(Expand).Prepend(type.GetGenericTypeDefinition());
        }

        return [type];
    }

    private static IEnumerable<Type> BodyReferences(MethodBase method)
    {
        byte[]? il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            yield break;
        }

        Type[]? typeArguments = method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null;
        Type[]? methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        int position = 0;
        while (position < il.Length)
        {
            short value = il[position] == 0xFE ? (short)(0xFE00 | il[position + 1]) : il[position];
            position += il[position] == 0xFE ? 2 : 1;
            OpCode code = OpCodesByValue[value];
            if (code.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok)
            {
                int token = BitConverter.ToInt32(il, position);
                MemberInfo? member = Resolve(method.Module, token, typeArguments, methodArguments);
                if (member is Type resolvedType)
                {
                    yield return resolvedType;
                }
                else if (member?.DeclaringType is { } owner)
                {
                    yield return owner;
                    if (member is MethodInfo called)
                    {
                        yield return called.ReturnType;
                        foreach (ParameterInfo parameter in called.GetParameters())
                        {
                            yield return parameter.ParameterType;
                        }
                    }
                    else if (member is FieldInfo field)
                    {
                        yield return field.FieldType;
                    }
                }
            }

            position += OperandSize(code, il, position);
        }
    }

    private static MemberInfo? Resolve(Module module, int token, Type[]? typeArguments, Type[]? methodArguments)
    {
        try
        {
            return module.ResolveMember(token, typeArguments, methodArguments);
        }
        catch (ArgumentException)
        {
            return null; // a token for an open generic context the resolver cannot bind; its parts show in signatures
        }
    }

    private static int OperandSize(OpCode code, byte[] il, int position) => code.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, position)),
        _ => 4,
    };
}
