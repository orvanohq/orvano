namespace Orvano.Auth.Domain;

/// <summary>What a flow is for (spec 0012, the <c>purpose</c> column): signing in, or linking to the signed in user.</summary>
internal enum FlowPurpose
{
    SignIn,
    Link,
}

/// <summary>The wire names of <see cref="FlowPurpose"/>, and the <c>orvano_type</c> each one returns to the app.</summary>
internal static class FlowPurposes
{
    public const string SignIn = "sign_in";
    public const string Link = "link";

    public static string Wire(FlowPurpose purpose) => purpose switch
    {
        FlowPurpose.SignIn => SignIn,
        FlowPurpose.Link => Link,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null),
    };

    public static FlowPurpose Parse(string value) => value switch
    {
        SignIn => FlowPurpose.SignIn,
        Link => FlowPurpose.Link,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown flow purpose."),
    };

    /// <summary>The <c>orvano_type</c> value (AC-5, the contract's <c>OAuthLinkType</c>).</summary>
    public static string LinkType(FlowPurpose purpose) => purpose == FlowPurpose.Link ? "oauth_link" : "oauth";
}

/// <summary>
/// Where the callback sends the browser back (AC-5): the flow's redirect URL with <c>orvano_type</c>, then
/// <c>orvano_code</c> or <c>orvano_error</c>, replacing any parameters of those names.
/// </summary>
internal static class AppRedirect
{
    public const string TypeParameter = LinkUrl.TypeParameter;
    public const string CodeParameter = "orvano_code";
    public const string ErrorParameter = "orvano_error";

    private static readonly string[] Ours = [TypeParameter, CodeParameter, ErrorParameter];

    public static string Success(Uri redirect, FlowPurpose purpose, HandoffCode code) =>
        LinkUrl.With(redirect, Ours, [(TypeParameter, FlowPurposes.LinkType(purpose)), (CodeParameter, code.Value)]);

    /// <summary><paramref name="errorCode"/> is one of the contract's error codes (AC-6).</summary>
    public static string Failure(Uri redirect, FlowPurpose purpose, string errorCode) =>
        LinkUrl.With(redirect, Ours, [(TypeParameter, FlowPurposes.LinkType(purpose)), (ErrorParameter, errorCode)]);
}
