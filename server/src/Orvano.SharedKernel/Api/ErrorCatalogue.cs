using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Orvano.SharedKernel.Api;

/// <summary>
/// Every error code the server can send (spec 0014 XTN-1, XTN-3): <see cref="ErrorCodes"/> plus each module's
/// <c>&lt;Module&gt;ErrorCodes</c> registered with <see cref="ErrorCatalogueExtensions.AddErrorCodes"/>. The contract
/// writes it as <c>x-orvano-error-codes</c>, and the startup check refuses an operation that lists a code not in it.
/// </summary>
public sealed class ErrorCatalogue
{
    public ErrorCatalogue(IEnumerable<ErrorCodeSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var entries = new SortedDictionary<string, ErrorCodeAttribute>(StringComparer.Ordinal);
        foreach (Type type in sources.Select(source => source.Type).Prepend(typeof(ErrorCodes)).Distinct())
        {
            foreach ((string code, ErrorCodeAttribute entry) in ErrorCodes.Catalogue(type))
            {
                if (!ErrorCodes.IsWellFormed(code))
                {
                    throw new InvalidOperationException($"{type.Name} declares '{code}', which is not a well formed error code (PRB-7).");
                }

                if (!entries.TryAdd(code, entry))
                {
                    throw new InvalidOperationException($"{type.Name} declares '{code}', which another catalogue already declares (PRB-7).");
                }
            }
        }

        Entries = entries;
    }

    /// <summary>Each code and its entry, sorted by code.</summary>
    public IReadOnlyDictionary<string, ErrorCodeAttribute> Entries { get; }

    public bool Contains(string code) => Entries.ContainsKey(code);
}

/// <summary>A class of <see cref="ErrorCodeAttribute"/> marked constants that joins the <see cref="ErrorCatalogue"/>.</summary>
public sealed record ErrorCodeSource(Type Type);

public static class ErrorCatalogueExtensions
{
    /// <summary>
    /// Adds the codes declared on <paramref name="type"/> (a module's <c>&lt;Module&gt;ErrorCodes</c>) to the catalogue.
    /// Call it in <c>Add&lt;Module&gt;Module</c>; an operation can list only catalogued codes in <c>WithErrors</c>.
    /// </summary>
    public static IServiceCollection AddErrorCodes(this IServiceCollection services, Type type)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(type);
        services.AddSingleton(new ErrorCodeSource(type));
        services.AddErrorCatalogue();
        return services;
    }

    /// <summary>Registers the <see cref="ErrorCatalogue"/> over the shared codes and every registered source.</summary>
    public static IServiceCollection AddErrorCatalogue(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ErrorCatalogue>();
        return services;
    }
}
