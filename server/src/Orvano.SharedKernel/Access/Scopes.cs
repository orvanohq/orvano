namespace Orvano.SharedKernel.Access;

/// <summary>The two API key types (spec 0006 KEY-1), stored as <c>platform.api_key.type</c>.</summary>
public enum KeyType
{
    Publishable,
    Secret,
}

/// <summary>
/// The API key scope catalog and the ceiling per key type (spec 0012 SCP-1 to SCP-4). Adding a scope is a code change
/// here plus a CHECK swap on <c>platform.api_key</c> in that feature's migration (SCP-3).
/// </summary>
public static class Scopes
{
    public const string DatabasesRead = "databases.read";
    public const string DatabasesWrite = "databases.write";
    public const string StorageRead = "storage.read";
    public const string StorageWrite = "storage.write";
    public const string UsersRead = "users.read";
    public const string UsersWrite = "users.write";
    public const string FunctionsRead = "functions.read";
    public const string FunctionsWrite = "functions.write";
    public const string FunctionsExecute = "functions.execute";
    public const string MessagingRead = "messaging.read";
    public const string MessagingWrite = "messaging.write";
    public const string RealtimeSubscribe = "realtime.subscribe";
    public const string SitesRead = "sites.read";
    public const string SitesWrite = "sites.write";

    public const string ClientAuth = "client.auth";
    public const string ClientAuthSignup = "client.auth.signup";
    public const string ClientDatabases = "client.databases";
    public const string ClientStorage = "client.storage";
    public const string ClientFunctions = "client.functions";
    public const string ClientRealtime = "client.realtime";

    /// <summary>SCP-4: kept for acting as a user from a server key; refused until a spec defines it.</summary>
    public const string UsersImpersonate = "users.impersonate";

    /// <summary>The field the validation errors name.</summary>
    public const string Field = "scopes";

    /// <summary>SCP-1: the secret ceiling. Run keys (FNC-2) share it.</summary>
    public static IReadOnlySet<string> Secret { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        DatabasesRead, DatabasesWrite, StorageRead, StorageWrite, UsersRead, UsersWrite, FunctionsRead, FunctionsWrite,
        FunctionsExecute, MessagingRead, MessagingWrite, RealtimeSubscribe, SitesRead, SitesWrite,
    };

    /// <summary>SCP-2: the publishable ceiling, each one a client API switch. A new publishable key gets all of them.</summary>
    public static IReadOnlySet<string> Publishable { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ClientAuth, ClientAuthSignup, ClientDatabases, ClientStorage, ClientFunctions, ClientRealtime,
    };

    public static IReadOnlySet<string> CeilingFor(KeyType type) => type switch
    {
        KeyType.Publishable => Publishable,
        KeyType.Secret => Secret,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    /// <summary>
    /// SCP-3: every scope inside the type's ceiling, <c>users.impersonate</c> refused (SCP-4), and
    /// <c>client.auth.signup</c> only with <c>client.auth</c> (SCP-2). Returns the scopes deduplicated and sorted.
    /// </summary>
    public static Result<string[]> Validate(KeyType type, IEnumerable<string> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        IReadOnlySet<string> ceiling = CeilingFor(type);
        var problems = new List<string>();
        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string scope in scopes)
        {
            if (scope == UsersImpersonate)
            {
                problems.Add($"'{scope}': this scope is reserved for later and cannot be used yet.");
            }
            else if (!ceiling.Contains(scope))
            {
                problems.Add($"'{scope}': not a scope a {(type == KeyType.Secret ? "secret" : "publishable")} key can hold.");
            }
            else
            {
                result.Add(scope);
            }
        }

        if (result.Contains(ClientAuthSignup) && !result.Contains(ClientAuth))
        {
            problems.Add($"'{ClientAuthSignup}': needs '{ClientAuth}' too.");
        }

        return problems.Count > 0 ? AccessErrors.Invalid(Field, problems) : result.ToArray();
    }

    /// <summary>KEY-4: the per request recheck of stored scopes against the ceiling.</summary>
    public static bool WithinCeiling(KeyType type, IEnumerable<string> scopes) => Validate(type, scopes).IsSuccess;
}
