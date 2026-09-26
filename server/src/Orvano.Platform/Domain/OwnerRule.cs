namespace Orvano.Platform.Domain;

/// <summary>What deleting a console account means for one org it owns (AC-10).</summary>
internal enum AccountOrgOutcome
{
    /// <summary>Another owner remains, so the account's membership simply goes.</summary>
    Unaffected,

    /// <summary>The account is the only member and no live project remains: the org moves to deleting with it.</summary>
    DeleteWithAccount,

    /// <summary>The account is the last owner of an org with other members or live projects: the delete is refused.</summary>
    Blocks,
}

/// <summary>An org always keeps at least one owner (AC-10).</summary>
internal static class OwnerRule
{
    /// <summary>Whether an owner can be removed, demoted, or leave, given how many owners the org has.</summary>
    public static bool CanLoseOwner(int ownerCount) => ownerCount > 1;

    /// <summary>The outcome for one org when an account that is one of its owners is deleted.</summary>
    /// <param name="ownerCount">Owners of the org, the account included.</param>
    /// <param name="memberCount">Members of the org, the account included.</param>
    /// <param name="liveProjectCount">Projects of the org that are not deleting.</param>
    public static AccountOrgOutcome OnAccountDeleted(int ownerCount, int memberCount, int liveProjectCount)
    {
        if (CanLoseOwner(ownerCount)) return AccountOrgOutcome.Unaffected;
        return memberCount <= 1 && liveProjectCount == 0 ? AccountOrgOutcome.DeleteWithAccount : AccountOrgOutcome.Blocks;
    }
}
