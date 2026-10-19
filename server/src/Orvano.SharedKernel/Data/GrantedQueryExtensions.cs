using System.Linq.Expressions;
using System.Reflection;

namespace Orvano.SharedKernel.Data;

/// <summary>
/// The PostgreSQL side of spec 0012 ENF-1, for platform tables such as <c>storage.file</c>: the row filter runs in
/// the query, never in memory. It lives here, not in <c>Access/</c>, so the access core stays engine neutral (ENG-1).
/// </summary>
public static class GrantedQueryExtensions
{
    private static readonly MethodInfo AnyWithPredicate = typeof(Enumerable).GetMethods()
        .Single(method => method.Name == nameof(Enumerable.Any) && method.GetParameters().Length == 2)
        .MakeGenericMethod(typeof(string));

    private static readonly MethodInfo Contains = typeof(Enumerable).GetMethods()
        .Single(method => method.Name == nameof(Enumerable.Contains) && method.GetParameters().Length == 2)
        .MakeGenericMethod(typeof(string));

    /// <summary>
    /// Keeps rows whose grant array overlaps <paramref name="tokens"/> (from <c>Caller.MatchTokens</c>). Npgsql
    /// translates it to <c>column &amp;&amp; @tokens</c>, which a GIN index on the column serves. The tokens go in as
    /// one parameter, so every caller shares one query plan.
    /// </summary>
    public static IQueryable<T> WhereGranted<T>(
        this IQueryable<T> source,
        Expression<Func<T, string[]>> column,
        IReadOnlyCollection<string> tokens)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(tokens);

        // A member of a closure object is a query parameter to EF; a bare constant array would be inlined.
        var holder = new TokenHolder([.. tokens]);
        MemberExpression parameter = Expression.Property(Expression.Constant(holder), nameof(TokenHolder.Tokens));
        ParameterExpression token = Expression.Parameter(typeof(string), "token");
        LambdaExpression isToken = Expression.Lambda(Expression.Call(Contains, parameter, token), token);
        MethodCallExpression overlaps = Expression.Call(AnyWithPredicate, column.Body, isToken);

        return source.Where(Expression.Lambda<Func<T, bool>>(overlaps, column.Parameters));
    }

    private sealed record TokenHolder(string[] Tokens);
}
