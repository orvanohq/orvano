using Orvano.Contract;
using Orvano.Databases.Domain;

namespace Orvano.Databases.Application;

/// <summary>
/// A refusal: the HTTP status, a stable error code from the contract's catalog, a short safe sentence built from
/// names only (never a value or Postgres' own text, spec 0015 AC-20), the bad fields of an <c>invalid_row</c>, and a
/// <c>Retry-After</c> in seconds when retrying helps.
/// </summary>
internal sealed record Failure(int Status, string Code, string Detail, IReadOnlyList<FieldProblem>? Fields = null, int? RetryAfter = null)
{
    public static Failure Invalid(string detail) => new(400, ErrorCode.InvalidRequest, detail);

    public static Failure ProjectHeaderMissing { get; } = Invalid("Send the project ID in the X-Orvano-Project header.");

    public static Failure ProjectNotFound { get; } = new(404, ErrorCode.ProjectNotFound, "No such project.");

    public static Failure ProjectNotReady { get; } = new(409, ErrorCode.ProjectNotReady, "The project is not ready yet.");

    public static Failure Forbidden { get; } = new(403, ErrorCode.Forbidden, "Developers and owners only.");

    public static Failure AccessDenied { get; } = new(
        403, ErrorCode.TableAccessDenied, "Only API keys can use tables until they have permission rules.");

    public static Failure InvalidCursor { get; } = new(400, ErrorCode.InvalidCursor, "The cursor is not one this server issued for this listing.");

    public static Failure InvalidLimit { get; } = Invalid("limit: use 1 to 100.");

    public static Failure DatabaseNotFound { get; } = new(404, ErrorCode.DatabaseNotFound, "No such database in the project.");

    public static Failure DatabaseNotReady { get; } = new(409, ErrorCode.DatabaseNotReady, "The database is not ready: it is still being created, or creating it failed.");

    public static Failure TableNotFound { get; } = new(404, ErrorCode.TableNotFound, "No such table in the database.");

    public static Failure NameTaken(string name) => new(409, ErrorCode.NameTaken, $"name: a table named '{name}' already exists in this database.");

    public static Failure TableLimit { get; } = new(409, ErrorCode.SchemaLimitReached, $"A database holds at most {DataLimits.Tables} tables.");

    public static Failure TableBusy { get; } = new(409, ErrorCode.TableBusy, "Another change holds the table; retry in a moment.", RetryAfter: 1);

    public static Failure QueryTimeout { get; } = new(422, ErrorCode.QueryTimeout, "The query ran past its time limit.");

    public static Failure NoPrimaryKey { get; } = new(409, ErrorCode.TableNotWritable, "The table needs a primary key to be used through the API.");

    public static Failure ReadOnlyTable { get; } = new(
        409, ErrorCode.TableNotWritable, "The table's primary key is not the standard id uuid, so its rows are read only through the API.");

    public static Failure NotOwned { get; } = new(409, ErrorCode.TableNotWritable, "The project does not own this table, so the API can't change it.");

    public static Failure InvalidRow(IReadOnlyList<FieldProblem> fields) =>
        new(400, ErrorCode.InvalidRow, $"{fields[0].Field}: {fields[0].Message}", fields);

    public static Failure RowConflict(string what) => new(409, ErrorCode.RowConflict, what);
}

/// <summary>A use case's result: a value, or a <see cref="Failure"/>.</summary>
internal readonly struct Outcome<T>
{
    private Outcome(T? value, Failure? failure)
    {
        Value = value;
        Failure = failure;
    }

    public T? Value { get; }

    public Failure? Failure { get; }

    public bool Succeeded => Failure is null;

    public static implicit operator Outcome<T>(T value) => new(value, null);

    public static implicit operator Outcome<T>(Failure failure) => new(default, failure);
}

/// <summary>Thrown inside a unit of work to roll it back and answer <see cref="Failure"/>; caught by <see cref="DataScope"/>.</summary>
internal sealed class FailureException(Failure failure) : Exception(failure.Detail)
{
    public Failure Failure { get; } = failure;
}
