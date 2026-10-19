namespace Orvano.SharedKernel.Access;

/// <summary>The one kind every request resolves to (spec 0012 CLR-1).</summary>
public enum CallerKind
{
    /// <summary>A publishable key and no end user session.</summary>
    Guest,

    /// <summary>A publishable key plus a verified access token, anonymous sessions included.</summary>
    EndUser,

    SecretKey,

    /// <summary>A function run's key (spec 0012 FNC-2).</summary>
    RunKey,

    /// <summary>A console session.</summary>
    Operator,

    /// <summary>Jobs and startup; never reachable from HTTP.</summary>
    System,
}

/// <summary>
/// Who is calling (spec 0012 CLR-1 to CLR-3). Built only through the factories, so the role set always matches the
/// kind. <see cref="Scopes"/> holds a key's stored scopes: client APIs for guests and end users (SCP-2), server
/// scopes for secret and run keys (SCP-1). Operators carry none; console permissions come from their membership.
/// A class, not a record: it holds sets and arrays by reference, so two callers are the same only by reference.
/// </summary>
public sealed class Caller
{
    private static readonly IReadOnlySet<string> NoScopes = new HashSet<string>(StringComparer.Ordinal);

    private readonly string[] _roleTokens;

    private Caller(
        CallerKind kind,
        Guid? projectId,
        Guid? userId,
        IReadOnlyList<string> labels,
        IReadOnlySet<string> scopes,
        Guid? operatorId)
    {
        Kind = kind;
        ProjectId = projectId;
        UserId = userId;
        Labels = labels;
        Scopes = scopes;
        OperatorId = operatorId;
        Roles = RolesFor(kind, userId, labels);
        _roleTokens = [.. Roles.Select(role => role.ToString())];
    }

    /// <summary>The caller with no HTTP request behind it (CLR-3).</summary>
    public static Caller System { get; } = new(CallerKind.System, null, null, [], NoScopes, null);

    public CallerKind Kind { get; }

    public Guid? ProjectId { get; }

    /// <summary>The end user (the token's <c>sub</c>), or for a run key the acting user (<c>act</c>), if any.</summary>
    public Guid? UserId { get; }

    /// <summary>The end user's current labels (LBL-2). Empty for every other kind.</summary>
    public IReadOnlyList<string> Labels { get; }

    public IReadOnlySet<string> Scopes { get; }

    public Guid? OperatorId { get; }

    /// <summary>CLR-3: secret keys, run keys, operators, and the system skip grants and are bounded otherwise.</summary>
    public bool BypassesGrants => Kind is CallerKind.SecretKey or CallerKind.RunKey or CallerKind.Operator or CallerKind.System;

    /// <summary>The roles grants are matched against (CLR-2). Empty for callers that bypass grants.</summary>
    public IReadOnlyList<Role> Roles { get; }

    public static Caller Guest(Guid projectId, IEnumerable<string> clientApis) =>
        new(CallerKind.Guest, projectId, null, [], ScopeSet(clientApis), null);

    /// <summary>An end user session; every label must be a valid label name (spec 0011 <c>auth.user.labels</c>).</summary>
    public static Caller EndUser(Guid projectId, Guid userId, IEnumerable<string> labels, IEnumerable<string> clientApis) =>
        new(CallerKind.EndUser, projectId, userId, [.. labels.Distinct(StringComparer.Ordinal)], ScopeSet(clientApis), null);

    public static Caller SecretKey(Guid projectId, IEnumerable<string> scopes) =>
        new(CallerKind.SecretKey, projectId, null, [], ScopeSet(scopes), null);

    /// <summary>A run key (FNC-2): <paramref name="scopes"/> from <c>scp</c>, <paramref name="actingUserId"/> from <c>act</c>.</summary>
    public static Caller RunKey(Guid projectId, IEnumerable<string> scopes, Guid? actingUserId) =>
        new(CallerKind.RunKey, projectId, actingUserId, [], ScopeSet(scopes), null);

    public static Caller Operator(Guid operatorId) =>
        new(CallerKind.Operator, null, null, [], NoScopes, operatorId);

    /// <summary>
    /// ENF-1: the tokens a row's or file's grants must overlap for <paramref name="action"/>, one per role. Callers that
    /// bypass grants never filter, so asking for theirs is a bug.
    /// </summary>
    public string[] MatchTokens(GrantAction action)
    {
        if (BypassesGrants)
        {
            throw new InvalidOperationException($"A {Kind} caller bypasses grants and has no tokens to match.");
        }

        string prefix = action.ToToken() + ":";
        return [.. _roleTokens.Select(role => prefix + role)];
    }

    /// <summary>Whether <paramref name="role"/> (as token text) is one of this caller's roles.</summary>
    public bool HasRole(ReadOnlySpan<char> role)
    {
        foreach (string own in _roleTokens)
        {
            if (role.SequenceEqual(own))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> ScopeSet(IEnumerable<string> scopes) => scopes.ToHashSet(StringComparer.Ordinal);

    private static Role[] RolesFor(CallerKind kind, Guid? userId, IReadOnlyList<string> labels) => kind switch
    {
        CallerKind.Guest => [Role.Any, Role.Guests],
        CallerKind.EndUser => [Role.Any, Role.Users, Role.User(userId!.Value), .. labels.Select(Role.Label)],
        _ => [],
    };
}
