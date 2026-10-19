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
    /// remove only tokens naming their own roles; stored tokens naming other roles are kept as they are (WRT-2). A
    /// client never writes table, bucket, or function grants (WRT-6): that answers <see cref="AccessErrors.Forbidden"/>
    /// before any token is read.
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
        if (!writer.BypassesGrants && target is not (GrantTarget.Row or GrantTarget.File))
        {
            return AccessErrors.Forbidden;
        }

        var problems = new InputProblems();
        var tokens = new SortedSet<string>(StringComparer.Ordinal);
        int read = 0;

        foreach (string raw in input)
        {
            if (++read > InputProblems.MaxEntries)
            {
                problems.AddTooLong();
                break;
            }

            foreach (string token in Expand(raw, allowed, problems))
            {
                if (!Grant.TryParse(token, out Grant grant) || grant.Role.IsDefault)
                {
                    problems.Add(DescribeBadToken(raw));
                }
                else if (!allowed.Contains(grant.Action))
                {
                    problems.Add($"{InputProblems.Quote(raw)}: {grant.Action.ToToken()} is not a valid grant on a {Describe(target)}.");
                }
                else if (!writer.BypassesGrants && !writer.HasRole(grant.Role.ToString()) && existing?.Contains(token) != true)
                {
                    problems.Add($"{InputProblems.Quote(raw)}: you can only grant roles you hold.");
                }
                else
                {
                    tokens.Add(token);
                }
            }

            if (problems.IsFull)
            {
                break;
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

        return problems.Count > 0 ? AccessErrors.Invalid(Field, problems.Items) : tokens.ToArray();
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

    // GRM-4: write:<role> becomes the write actions the target allows. It is never stored. The role is checked once
    // here, so a bad one is reported once, not once per expanded action.
    private static string[] Expand(string raw, IReadOnlySet<GrantAction> allowed, InputProblems problems)
    {
        if (raw is null || !raw.StartsWith(WritePrefix, StringComparison.Ordinal))
        {
            return [raw ?? string.Empty];
        }

        string role = raw[WritePrefix.Length..];
        if (!Role.TryParse(role, out _))
        {
            problems.Add(DescribeBadToken(raw));
            return [];
        }

        string[] expanded = [.. WriteActions.Where(allowed.Contains).Select(action => $"{action.ToToken()}:{role}")];
        if (expanded.Length == 0)
        {
            problems.Add($"{InputProblems.Quote(raw)}: write is not a valid grant here.");
        }

        return expanded;
    }

    private static string DescribeBadToken(string? raw)
    {
        int colon = raw?.IndexOf(':', StringComparison.Ordinal) ?? -1;
        return colon > 0 && Role.IsReserved(raw![(colon + 1)..])
            ? $"{InputProblems.Quote(raw)}: this role is reserved for later and cannot be used yet."
            : $"{InputProblems.Quote(raw)}: not a valid grant. Use <action>:<role>, for example read:users.";
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
