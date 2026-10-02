using Orvano.Auth.Domain;

namespace Orvano.Auth.Application;

/// <summary>The events every email flow shares (AC-30).</summary>
internal static class EmailEvents
{
    /// <summary><c>auth.email_token.created</c>: the kind, the user (null for an unknown email), and the actor; never the email.</summary>
    public static Task TokenCreatedAsync(AuthUnitOfWork uow, string projectId, EmailTokenKind kind, Guid? userId, Actor actor, CancellationToken ct) =>
        AuthEvents.WriteAsync(uow.Tx, AuthEvents.EmailTokenCreated, projectId, actor, userId?.ToString() ?? projectId,
            new Dictionary<string, string>(),
            fields: new Dictionary<string, string?> { ["kind"] = EmailTokenKinds.Wire(kind), ["userId"] = userId?.ToString() }, ct: ct);
}
