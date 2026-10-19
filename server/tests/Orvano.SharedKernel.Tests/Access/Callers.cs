using Orvano.SharedKernel.Access;

namespace Orvano.SharedKernel.Tests.Access;

/// <summary>Sample callers for the access tests: users A and B in one project, with every client API on.</summary>
internal static class Callers
{
    public static readonly Guid Project = Guid.Parse("0192f7a4-0000-7000-8000-000000000001");
    public static readonly Guid UserA = Guid.Parse("0192f7a4-0000-7000-8000-00000000000a");
    public static readonly Guid UserB = Guid.Parse("0192f7a4-0000-7000-8000-00000000000b");

    public static Caller Guest { get; } = Caller.Guest(Project, Scopes.Publishable);

    public static Caller A { get; } = Caller.EndUser(Project, UserA, [], Scopes.Publishable);

    public static Caller B { get; } = Caller.EndUser(Project, UserB, [], Scopes.Publishable);

    public static Caller AdminA { get; } = Caller.EndUser(Project, UserA, ["admin"], Scopes.Publishable);

    public static Caller Secret { get; } = Caller.SecretKey(Project, Scopes.Secret);

    public static Caller Run { get; } = Caller.RunKey(Project, [Scopes.DatabasesRead], UserA);

    public static Caller Operator { get; } = Caller.Operator(Guid.Parse("0192f7a4-0000-7000-8000-0000000000ff"));

    public static string UserRole(Guid id) => "user:" + id.ToString("D");
}
