using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orvano.Databases.Application;
using Orvano.Databases.Contracts;
using Orvano.Databases.Domain;
using static Orvano.Databases.Endpoints.ApiMapping;
using Api = Orvano.Contract;

namespace Orvano.Databases.Endpoints;

/// <summary>
/// The public data API (spec 0015) and its console mirrors, each a thin adapter. Public calls check their caller
/// first (<see cref="DataRequests"/>), then the database, the table, the input, and run. Console calls (the host's
/// session check has already run) check the member and role, then the same.
/// </summary>
internal static class DataEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        MapDatabases(v1);
        MapTables(v1);
        MapRows(v1);
        MapConsole(v1);
    }

    private static void MapDatabases(RouteGroupBuilder v1)
    {
        v1.MapGet(Api.DatabasesOperations.List.Route, async (HttpContext http, DatabaseDirectory databases, string? cursor, int? limit, CancellationToken ct) =>
        {
            var (caller, refusal) = await DataRequests.ServerKeyAsync(http, Api.DatabasesOperations.List.Scope);
            return caller is null ? refusal! : Ok(http, await databases.ListAsync(caller.ProjectId, cursor, limit, ct), DatabasePage);
        })
            .WithName(Api.DatabasesOperations.List.Id);

        v1.MapGet(Api.DatabasesOperations.Get.Route, async (HttpContext http, string database) =>
        {
            var (caller, refusal) = await DataRequests.ServerKeyAsync(http, Api.DatabasesOperations.Get.Scope);
            return caller is null ? refusal! : Ok(http, await DataRequests.DatabaseAsync(http, caller.ProjectId, database, mustBeActive: false), Database);
        })
            .WithName(Api.DatabasesOperations.Get.Id);
    }

    private static void MapTables(RouteGroupBuilder v1)
    {
        v1.MapGet(Api.TablesOperations.List.Route, async (HttpContext http, TableService tables, string database, string? cursor, int? limit, CancellationToken ct) =>
        {
            var (caller, refusal) = await DataRequests.ServerKeyAsync(http, Api.TablesOperations.List.Scope);
            return caller is null ? refusal! : await ListTablesAsync(http, tables, caller, database, cursor, limit, ct);
        })
            .WithName(Api.TablesOperations.List.Id);

        v1.MapGet(Api.TablesOperations.Get.Route, async (HttpContext http, TableService tables, string database, string table, CancellationToken ct) =>
        {
            var (caller, refusal) = await DataRequests.ServerKeyAsync(http, Api.TablesOperations.Get.Scope);
            return caller is null ? refusal! : await GetTableAsync(http, tables, caller, database, table, ct);
        })
            .WithName(Api.TablesOperations.Get.Id);

        v1.MapPost(Api.TablesOperations.Create.Route, async (
            HttpContext http, TableService tables, string database, Api.CreateTableRequest request, CancellationToken ct) =>
        {
            var (caller, refusal) = await DataRequests.ServerKeyAsync(http, Api.TablesOperations.Create.Scope);
            return caller is null ? refusal! : await CreateTableAsync(http, tables, caller, database, request, ct);
        })
            .WithName(Api.TablesOperations.Create.Id);
    }

    private static void MapRows(RouteGroupBuilder v1)
    {
        v1.MapGet(Api.RowsOperations.List.Route, async (
            HttpContext http, RowService rows, string database, string table, string? cursor, int? limit, CancellationToken ct) =>
        {
            var (caller, refusal) = await DataRequests.RowCallerAsync(http, Api.RowsOperations.List.Scope, database, table, RowOperation.List);
            return caller is null ? refusal! : await ListRowsAsync(http, rows, caller, database, table, cursor, limit, DataTimeouts.Public, ct);
        })
            .WithName(Api.RowsOperations.List.Id);

        // The body is read only after the caller checks pass, so a refused caller learns nothing from it; it is read
        // as a document, so bigint and decimal values keep their exact tokens (AC-16).
        v1.MapPost(Api.RowsOperations.Create.Route, async (HttpContext http, RowService rows, string database, string table, CancellationToken ct) =>
        {
            var (caller, refusal) = await DataRequests.RowCallerAsync(http, Api.RowsOperations.Create.Scope, database, table, RowOperation.Create);
            if (caller is null) return refusal!;

            var target = await DataRequests.DatabaseAsync(http, caller.ProjectId, database);
            if (!target.Succeeded) return Problem(http, target.Failure!);
            using var body = await ReadBodyAsync(http, ct);
            if (body is null) return Problem(http, Failure.InvalidRow([new FieldProblem("body", "expected a JSON object of column values")]));
            return Created(http, await rows.CreateAsync(caller.ProjectId, target.Value!, table, body.RootElement, caller.Actor, DataTimeouts.Public, ct), row => row);
        })
            .WithName(Api.RowsOperations.Create.Id);
    }

    private static void MapConsole(RouteGroupBuilder v1)
    {
        v1.MapGet(Api.ConsoleDatabasesOperations.List.Route, async (HttpContext http, DatabaseDirectory databases, string? cursor, int? limit, CancellationToken ct) =>
        {
            var (caller, refusal) = await DataRequests.ConsoleAsync(http, change: false);
            return caller is null ? refusal! : Ok(http, await databases.ListAsync(caller.ProjectId, cursor, limit, ct), DatabasePage);
        })
            .WithName(Api.ConsoleDatabasesOperations.List.Id);

        v1.MapGet(Api.ConsoleDatabasesOperations.Get.Route, async (HttpContext http, string database) =>
        {
            var (caller, refusal) = await DataRequests.ConsoleAsync(http, change: false);
            return caller is null ? refusal! : Ok(http, await DataRequests.DatabaseAsync(http, caller.ProjectId, database, mustBeActive: false), Database);
        })
            .WithName(Api.ConsoleDatabasesOperations.Get.Id);

        v1.MapGet(Api.ConsoleTablesOperations.List.Route, async (
            HttpContext http, TableService tables, string database, string? cursor, int? limit, CancellationToken ct) =>
        {
            var (caller, refusal) = await DataRequests.ConsoleAsync(http, change: false);
            return caller is null ? refusal! : await ListTablesAsync(http, tables, caller, database, cursor, limit, ct);
        })
            .WithName(Api.ConsoleTablesOperations.List.Id);

        v1.MapGet(Api.ConsoleTablesOperations.Get.Route, async (HttpContext http, TableService tables, string database, string table, CancellationToken ct) =>
        {
            var (caller, refusal) = await DataRequests.ConsoleAsync(http, change: false);
            return caller is null ? refusal! : await GetTableAsync(http, tables, caller, database, table, ct);
        })
            .WithName(Api.ConsoleTablesOperations.Get.Id);

        v1.MapPost(Api.ConsoleTablesOperations.Create.Route, async (
            HttpContext http, TableService tables, string database, Api.CreateTableRequest request, CancellationToken ct) =>
        {
            var (caller, refusal) = await DataRequests.ConsoleAsync(http, change: true);
            return caller is null ? refusal! : await CreateTableAsync(http, tables, caller, database, request, ct);
        })
            .WithName(Api.ConsoleTablesOperations.Create.Id);

        v1.MapGet(Api.ConsoleRowsOperations.List.Route, async (
            HttpContext http, RowService rows, string database, string table, string? cursor, int? limit, CancellationToken ct) =>
        {
            var (caller, refusal) = await DataRequests.ConsoleAsync(http, change: false);
            return caller is null ? refusal! : await ListRowsAsync(http, rows, caller, database, table, cursor, limit, DataTimeouts.Console, ct);
        })
            .WithName(Api.ConsoleRowsOperations.List.Id);
    }

    private static async Task<IResult> ListTablesAsync(
        HttpContext http, TableService tables, DataCaller caller, string database, string? cursor, int? limit, CancellationToken ct)
    {
        var target = await DataRequests.DatabaseAsync(http, caller.ProjectId, database);
        return target.Succeeded
            ? Ok(http, await tables.ListAsync(caller.ProjectId, target.Value!, cursor, limit, ct), TablePage(target.Value!))
            : Problem(http, target.Failure!);
    }

    private static async Task<IResult> GetTableAsync(HttpContext http, TableService tables, DataCaller caller, string database, string table, CancellationToken ct)
    {
        var target = await DataRequests.DatabaseAsync(http, caller.ProjectId, database);
        return target.Succeeded
            ? Ok(http, await tables.GetAsync(caller.ProjectId, target.Value!, table, ct), Table(target.Value!))
            : Problem(http, target.Failure!);
    }

    private static async Task<IResult> CreateTableAsync(
        HttpContext http, TableService tables, DataCaller caller, string database, Api.CreateTableRequest request, CancellationToken ct)
    {
        var target = await DataRequests.DatabaseAsync(http, caller.ProjectId, database);
        if (!target.Succeeded) return Problem(http, target.Failure!);
        var created = await tables.CreateAsync(caller.ProjectId, target.Value!, request.Name, Drafts(request.Columns), caller.Actor, ct);
        return Created(http, created, Table(target.Value!));
    }

    private static async Task<IResult> ListRowsAsync(
        HttpContext http, RowService rows, DataCaller caller, string database, string table, string? cursor, int? limit, DataTimeouts timeouts, CancellationToken ct)
    {
        var target = await DataRequests.DatabaseAsync(http, caller.ProjectId, database);
        return target.Succeeded
            ? Ok(http, await rows.ListAsync(caller.ProjectId, target.Value!, table, cursor, limit, timeouts, ct), RowPage)
            : Problem(http, target.Failure!);
    }

    /// <summary>The request body as a JSON document, or null when it is not JSON.</summary>
    private static async Task<JsonDocument?> ReadBodyAsync(HttpContext http, CancellationToken ct)
    {
        try
        {
            return await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
