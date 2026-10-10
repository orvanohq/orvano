using System.Globalization;
using Microsoft.AspNetCore.Http;
using Orvano.Core.Http;
using Orvano.Databases.Application;
using Orvano.Databases.Domain;
using Api = Orvano.Contract;

namespace Orvano.Databases.Endpoints;

/// <summary>Maps use case results to HTTP: structure to the generated contract models, failures to problems.</summary>
internal static class ApiMapping
{
    /// <summary>A failure as a problem, with its field errors and <c>Retry-After</c> when it has them.</summary>
    public static IResult Problem(HttpContext http, Failure failure)
    {
        if (failure.RetryAfter is { } seconds) http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        return failure.Fields is { Count: > 0 } fields
            ? ApiProblem.Result(failure.Status, failure.Code, failure.Detail, [.. fields.Select(f => new ApiProblem.FieldError(f.Field, f.Message))])
            : ApiProblem.Result(failure.Status, failure.Code, failure.Detail);
    }

    public static IResult Ok<T, TApi>(HttpContext http, Outcome<T> outcome, Func<T, TApi> map) =>
        outcome.Succeeded ? TypedResults.Ok(map(outcome.Value!)) : Problem(http, outcome.Failure!);

    public static IResult Created<T, TApi>(HttpContext http, Outcome<T> outcome, Func<T, TApi> map) =>
        outcome.Succeeded ? TypedResults.Json(map(outcome.Value!), statusCode: StatusCodes.Status201Created) : Problem(http, outcome.Failure!);

    public static Api.Database Database(DatabaseRef database) => new(
        database.Id,
        database.Slug,
        database.Name,
        database.State switch
        {
            DatabaseState.Provisioning => Api.DatabaseStatus.Provisioning,
            DatabaseState.Active => Api.DatabaseStatus.Active,
            DatabaseState.Failed => Api.DatabaseStatus.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(database), database.State, null),
        },
        database.Main,
        database.CreatedAt);

    public static Api.DatabasePage DatabasePage(DatabaseList page) => new([.. page.Items.Select(Database)], page.NextCursor);

    public static Func<TableInfo, Api.Table> Table(DatabaseRef database) => table => new Api.Table(
        table.Name,
        database.Slug,
        [.. table.Columns.Select(Column)],
        table.Readable,
        table.Writable,
        table.EstimatedRows);

    public static Func<TableList, Api.TablePage> TablePage(DatabaseRef database) =>
        page => new Api.TablePage([.. page.Items.Select(Table(database))], page.NextCursor);

    public static Api.RowPage RowPage(RowList page) => new(page.Items, page.NextCursor);

    private static Api.Column Column(ColumnInfo column) => new(
        column.Name,
        Type(column.Kind),
        column.PgType,
        column.Required,
        column.Unique,
        column.Default is { } d ? new Api.ColumnDefault(DefaultKind(d.Kind), d.Value) : null,
        column.System,
        column.Writable);

    /// <summary>The request's columns as the rules read them; a type or kind this server does not know reads as null.</summary>
    public static IReadOnlyList<ColumnDraft> Drafts(IReadOnlyList<Api.ColumnInput>? columns) =>
        [.. (columns ?? []).Select(c => new ColumnDraft(
            c.Name,
            Kind(c.Type),
            c.Required ?? false,
            c.Unique ?? false,
            c.Default is { } d ? new DefaultDraft(Kind(d.Kind), d.Value) : null))];

    private static Api.ColumnType Type(ColumnKind kind) => kind switch
    {
        ColumnKind.Text => Api.ColumnType.Text,
        ColumnKind.Integer => Api.ColumnType.Integer,
        ColumnKind.Bigint => Api.ColumnType.Bigint,
        ColumnKind.Float => Api.ColumnType.Float,
        ColumnKind.Decimal => Api.ColumnType.Decimal,
        ColumnKind.Boolean => Api.ColumnType.Boolean,
        ColumnKind.Timestamp => Api.ColumnType.Timestamp,
        ColumnKind.Date => Api.ColumnType.Date,
        ColumnKind.Uuid => Api.ColumnType.Uuid,
        ColumnKind.Json => Api.ColumnType.Json,
        ColumnKind.TextArray => Api.ColumnType.TextArray,
        ColumnKind.IntegerArray => Api.ColumnType.IntegerArray,
        ColumnKind.UuidArray => Api.ColumnType.UuidArray,
        ColumnKind.Other => Api.ColumnType.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static ColumnKind? Kind(Api.ColumnType type) => type switch
    {
        Api.ColumnType.Text => ColumnKind.Text,
        Api.ColumnType.Integer => ColumnKind.Integer,
        Api.ColumnType.Bigint => ColumnKind.Bigint,
        Api.ColumnType.Float => ColumnKind.Float,
        Api.ColumnType.Decimal => ColumnKind.Decimal,
        Api.ColumnType.Boolean => ColumnKind.Boolean,
        Api.ColumnType.Timestamp => ColumnKind.Timestamp,
        Api.ColumnType.Date => ColumnKind.Date,
        Api.ColumnType.Uuid => ColumnKind.Uuid,
        Api.ColumnType.Json => ColumnKind.Json,
        Api.ColumnType.TextArray => ColumnKind.TextArray,
        Api.ColumnType.IntegerArray => ColumnKind.IntegerArray,
        Api.ColumnType.UuidArray => ColumnKind.UuidArray,
        Api.ColumnType.Other => ColumnKind.Other,
        _ => null,
    };

    private static Api.DefaultKind DefaultKind(Domain.DefaultKind kind) => kind switch
    {
        Domain.DefaultKind.Value => Api.DefaultKind.Value,
        Domain.DefaultKind.Now => Api.DefaultKind.Now,
        Domain.DefaultKind.Uuidv7 => Api.DefaultKind.Uuidv7,
        Domain.DefaultKind.RandomUuid => Api.DefaultKind.RandomUuid,
        Domain.DefaultKind.Expression => Api.DefaultKind.Expression,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static Domain.DefaultKind? Kind(Api.DefaultKind kind) => kind switch
    {
        Api.DefaultKind.Value => Domain.DefaultKind.Value,
        Api.DefaultKind.Now => Domain.DefaultKind.Now,
        Api.DefaultKind.Uuidv7 => Domain.DefaultKind.Uuidv7,
        Api.DefaultKind.RandomUuid => Domain.DefaultKind.RandomUuid,
        Api.DefaultKind.Expression => Domain.DefaultKind.Expression,
        _ => null,
    };
}
