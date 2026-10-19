namespace Orvano.SharedKernel.Access;

/// <summary>
/// Turns grant input into the stored canonical array (spec 0012 GRM-4, WRT-2 to WRT-5): <c>write</c> expanded,
/// every token valid for its target, client writers held to their own roles, deduplicated, sorted, at most 100.
/// </summary>
public static class GrantSet
{
    public const int MaxTokens = 100;

    /// <summary>The request field the validation errors name.</summary>
    public const string Field = "permissions";

    private const string WritePrefix = "write:";

    private static readonly GrantAction[] WriteActions = [GrantAction.Create, GrantAction.Update, GrantAction.Delete];

    /// <summary>
    /// Normalizes <paramref name="input"/> written by <paramref name="writer"/> to <paramref name="target"/>.
    /// <paramref name="existing"/> is the stored array on an update (null on create). A guest or end user may add and
    /// remove only tokens naming their own roles; stored tokens naming other roles are kept as they are (WRT-2).
    /// </summary>
    public static Result<string[]> Normalize(
        IEnumerable<string> input,
        Caller writer,
        GrantTarget target,
        IReadOnlyCollection<string>? existing = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(writer);

        IReadOnlySet<GrantAction> allowed = target.AllowedActions();
        var problems = new List<string>();
        var tokens = new SortedSet<string>(StringComparer.Ordinal);

        foreach (string raw in input)
        {
            foreach (string token in Expand(raw, allowed, problems))
            {
                if (!Grant.TryParse(token, out Grant grant))
                {
                    problems.Add(DescribeBadToken(raw));
                }
                else if (!allowed.Contains(grant.Action))
                {
                    problems.Add($"'{raw}': {grant.Action.ToToken()} is not a valid grant on a {Describe(target)}.");
                }
                else if (!writer.BypassesGrants && !writer.HasRole(grant.Role.ToString()) && existing?.Contains(token) != true)
                {
                    problems.Add($"'{raw}': you can only grant roles you hold.");
                }
                else
                {
                    tokens.Add(token);
                }
            }
        }

        if (existing is not null && !writer.BypassesGrants)
        {
            // WRT-2: a client cannot remove what names a role it lacks (a server added read:user:<B>, for example).
            foreach (string kept in existing)
            {
                int colon = kept.IndexOf(':', StringComparison.Ordinal);
                if (colon > 0 && !writer.HasRole(kept.AsSpan(colon + 1)))
                {
                    tokens.Add(kept);
                }
            }
        }

        if (problems.Count == 0 && tokens.Count > MaxTokens)
        {
            problems.Add($"A grant list holds at most {MaxTokens} entries; this one has {tokens.Count}.");
        }

        return problems.Count > 0 ? AccessErrors.Invalid(Field, problems) : tokens.ToArray();
    }

    /// <summary>
    /// WRT-5: the grants a create gets when it sends no <c>permissions</c> field. An end user owns the rows and files
    /// they make; everyone else, and every table, bucket, or function, starts with none.
    /// </summary>
    public static string[] DefaultsFor(Caller creator, GrantTarget target)
    {
        ArgumentNullException.ThrowIfNull(creator);

        if (creator.Kind != CallerKind.EndUser || target is not (GrantTarget.Row or GrantTarget.File))
        {
            return [];
        }

        var owner = Role.User(creator.UserId!.Value);
        return
        [
            new Grant(GrantAction.Delete, owner).ToString(),
            new Grant(GrantAction.Read, owner).ToString(),
            new Grant(GrantAction.Update, owner).ToString(),
        ];
    }

    // GRM-4: write:<role> becomes the write actions the target allows. It is never stored.
    private static string[] Expand(string raw, IReadOnlySet<GrantAction> allowed, List<string> problems)
    {
        if (raw is null || !raw.StartsWith(WritePrefix, StringComparison.Ordinal))
        {
            return [raw ?? string.Empty];
        }

        string role = raw[WritePrefix.Length..];
        string[] expanded = [.. WriteActions.Where(allowed.Contains).Select(action => $"{action.ToToken()}:{role}")];
        if (expanded.Length == 0)
        {
            problems.Add($"'{raw}': write is not a valid grant here.");
        }

        return expanded;
    }

    private static string DescribeBadToken(string? raw)
    {
        int colon = raw?.IndexOf(':', StringComparison.Ordinal) ?? -1;
        return colon > 0 && Role.IsReserved(raw![(colon + 1)..])
            ? $"'{raw}': this role is reserved for later and cannot be used yet."
            : $"'{raw}': not a valid grant. Use <action>:<role>, for example read:users.";
    }

    private static string Describe(GrantTarget target) => target switch
    {
        GrantTarget.Table => "table",
        GrantTarget.Row => "row",
        GrantTarget.Bucket => "bucket",
        GrantTarget.File => "file",
        GrantTarget.Function => "function",
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
    };
}
