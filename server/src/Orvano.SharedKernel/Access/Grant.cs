using System.Diagnostics.CodeAnalysis;

namespace Orvano.SharedKernel.Access;

/// <summary>What a grant allows (spec 0012 GRM-1). Its token text is the lowercase name.</summary>
public enum GrantAction
{
    Read,
    Create,
    Update,
    Delete,
    Execute,
}

/// <summary>The kinds of thing that hold grants (spec 0012 EVL-4).</summary>
public enum GrantTarget
{
    Table,
    Row,
    Bucket,
    File,
    Function,
}

public static class GrantActions
{
    private static readonly IReadOnlySet<GrantAction> Container = new HashSet<GrantAction>
    {
        GrantAction.Read, GrantAction.Create, GrantAction.Update, GrantAction.Delete,
    };

    // EVL-3: create is decided at the table or bucket, so a row or file never holds it.
    private static readonly IReadOnlySet<GrantAction> Item = new HashSet<GrantAction>
    {
        GrantAction.Read, GrantAction.Update, GrantAction.Delete,
    };

    private static readonly IReadOnlySet<GrantAction> Function = new HashSet<GrantAction> { GrantAction.Execute };

    /// <summary>The actions a grant on <paramref name="target"/> may name (spec 0012 EVL-4).</summary>
    public static IReadOnlySet<GrantAction> AllowedActions(this GrantTarget target) => target switch
    {
        GrantTarget.Table or GrantTarget.Bucket => Container,
        GrantTarget.Row or GrantTarget.File => Item,
        GrantTarget.Function => Function,
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
    };

    public static string ToToken(this GrantAction action) => action switch
    {
        GrantAction.Read => "read",
        GrantAction.Create => "create",
        GrantAction.Update => "update",
        GrantAction.Delete => "delete",
        GrantAction.Execute => "execute",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    /// <summary>Strict and case sensitive (GRM-2): only the five lowercase names.</summary>
    public static bool TryParse(string? text, out GrantAction action)
    {
        (bool ok, action) = text switch
        {
            "read" => (true, GrantAction.Read),
            "create" => (true, GrantAction.Create),
            "update" => (true, GrantAction.Update),
            "delete" => (true, GrantAction.Delete),
            "execute" => (true, GrantAction.Execute),
            _ => (false, default),
        };
        return ok;
    }
}

/// <summary>
/// One grant, <c>&lt;action&gt;:&lt;role&gt;</c> (spec 0012 GRM-1). The token text is the stored form everywhere.
/// <c>write:</c> is input only and is expanded by <see cref="GrantSet.Normalize"/>, so it never parses here (GRM-4).
/// </summary>
public readonly record struct Grant(GrantAction Action, Role Role)
{
    public static bool TryParse([NotNullWhen(true)] string? token, out Grant grant)
    {
        grant = default;
        int colon = token?.IndexOf(':', StringComparison.Ordinal) ?? -1;
        if (colon <= 0
            || !GrantActions.TryParse(token![..colon], out GrantAction action)
            || !Role.TryParse(token[(colon + 1)..], out Role role))
        {
            return false;
        }

        grant = new Grant(action, role);
        return true;
    }

    public override string ToString() => $"{Action.ToToken()}:{Role}";
}
