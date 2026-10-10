// The data API (spec 0015): databases, tables, and rows of the project named by X-Orvano-Project. Database and table
// operations are for servers with an API key. Row operations also take an app user's access token or no credential,
// but until permission rules exist (scope row 17) only API keys get through; everyone else gets 403
// `table_access_denied`.
import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";
import "../auth/security.tsp";
import "./models.tsp";

using Http;
using OpenAPI;

namespace Orvano;

@route("/v1/databases")
@tag("databases")
@useAuth(apiKey)
interface DatabasesApi {
  @doc("Lists the project's databases: `main` first, then the rest by slug.")
  @operationId("databases.list")
  @extension("x-orvano-audience", "server")
  @extension("x-orvano-service", "databases")
  @extension("x-orvano-scope", "tables.read")
  @get
  list(
    @doc("The `nextCursor` of the previous page.")
    @query
    cursor?: string,

    @doc("Databases per page, 1 to 100. Defaults to 25.")
    @query
    limit?: int32,
  ): DatabasePage | Problem;

  @doc("Gets a database by slug.")
  @operationId("databases.get")
  @extension("x-orvano-audience", "server")
  @extension("x-orvano-service", "databases")
  @extension("x-orvano-scope", "tables.read")
  @route("/{database}")
  @get
  get(
    @doc("The database slug, such as `main`.")
    @path
    database: string,
  ): Database | Problem;
}

@route("/v1/databases/{database}/tables")
@tag("tables")
@useAuth(apiKey)
interface TablesApi {
  @doc("Lists a database's tables by name, with their columns, read live from Postgres.")
  @operationId("tables.list")
  @extension("x-orvano-audience", "server")
  @extension("x-orvano-service", "tables")
  @extension("x-orvano-scope", "tables.read")
  @get
  list(
    @doc("The database slug, such as `main`.")
    @path
    database: string,

    @doc("The `nextCursor` of the previous page.")
    @query
    cursor?: string,

    @doc("Tables per page, 1 to 100. Defaults to 25.")
    @query
    limit?: int32,
  ): TablePage | Problem;

  @doc("Gets a table with its columns, read live from Postgres.")
  @operationId("tables.get")
  @extension("x-orvano-audience", "server")
  @extension("x-orvano-service", "tables")
  @extension("x-orvano-scope", "tables.read")
  @route("/{table}")
  @get
  get(
    @doc("The database slug, such as `main`.")
    @path
    database: string,

    @doc("The table name.")
    @path
    table: string,
  ): Table | Problem;

  @doc("""
    Creates a table with `id uuid` (the primary key, `uuidv7()` by default), `created_at`, `updated_at`, then your
    columns in order.
    """)
  @operationId("tables.create")
  @extension("x-orvano-audience", "server")
  @extension("x-orvano-service", "tables")
  @extension("x-orvano-scope", "tables.write")
  @post
  create(
    @doc("The database slug, such as `main`.")
    @path
    database: string,

    @body request: CreateTableRequest,
  ): {
    @statusCode statusCode: 201;
    @body body: Table;
  } | Problem;
}

@route("/v1/databases/{database}/tables/{table}/rows")
@tag("rows")
@useAuth(NoAuth | bearer | apiKey)
interface RowsApi {
  @doc("""
    Lists rows, by `created_at` then `id` ascending. An API key needs `rows.read`; other callers are refused until
    the table has permission rules.
    """)
  @operationId("rows.list")
  @extension("x-orvano-audience", "both")
  @extension("x-orvano-service", "rows")
  @extension("x-orvano-scope", "rows.read")
  @get
  list(
    @doc("The database slug, such as `main`.")
    @path
    database: string,

    @doc("The table name.")
    @path
    table: string,

    @doc("The `nextCursor` of the previous page. It works only with the same database and table.")
    @query
    cursor?: string,

    @doc("Rows per page, 1 to 100. Defaults to 25.")
    @query
    limit?: int32,
  ): RowPage | Problem;

  @doc("""
    Creates a row and answers it as stored. `id` may be sent (a uuid); `created_at` and `updated_at` never. An API key
    needs `rows.write`; other callers are refused until the table has permission rules.
    """)
  @operationId("rows.create")
  @extension("x-orvano-audience", "both")
  @extension("x-orvano-service", "rows")
  @extension("x-orvano-scope", "rows.write")
  @post
  create(
    @doc("The database slug, such as `main`.")
    @path
    database: string,

    @doc("The table name.")
    @path
    table: string,

    @body row: Row,
  ): {
    @statusCode statusCode: 201;
    @body body: Row;
  } | Problem;
}

@route("/v1/console/project/databases")
@tag("console")
@useAuth(consoleSession)
interface ConsoleDatabasesApi {
  @doc("Lists the databases of the project named by `X-Orvano-Project`: `main` first, then by slug. Any member.")
  @operationId("consoleDatabases.list")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleDatabases")
  @get
  list(
    @doc("The `nextCursor` of the previous page.")
    @query
    cursor?: string,

    @doc("Databases per page, 1 to 100. Defaults to 25.")
    @query
    limit?: int32,
  ): DatabasePage | Problem;

  @doc("Gets a database of the project named by `X-Orvano-Project`. Any member.")
  @operationId("consoleDatabases.get")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleDatabases")
  @route("/{database}")
  @get
  get(
    @doc("The database slug, such as `main`.")
    @path
    database: string,
  ): Database | Problem;
}

@route("/v1/console/project/databases/{database}/tables")
@tag("console")
@useAuth(consoleSession)
interface ConsoleTablesApi {
  @doc("Lists a database's tables by name. Any member.")
  @operationId("consoleTables.list")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleTables")
  @get
  list(
    @doc("The database slug, such as `main`.")
    @path
    database: string,

    @doc("The `nextCursor` of the previous page.")
    @query
    cursor?: string,

    @doc("Tables per page, 1 to 100. Defaults to 25.")
    @query
    limit?: int32,
  ): TablePage | Problem;

  @doc("Gets a table with its columns. Any member.")
  @operationId("consoleTables.get")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleTables")
  @route("/{table}")
  @get
  get(
    @doc("The database slug, such as `main`.")
    @path
    database: string,

    @doc("The table name.")
    @path
    table: string,
  ): Table | Problem;

  @doc("Creates a table, as `tables.create` does. Owners and developers.")
  @operationId("consoleTables.create")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleTables")
  @post
  create(
    @doc("The database slug, such as `main`.")
    @path
    database: string,

    @body request: CreateTableRequest,
  ): {
    @statusCode statusCode: 201;
    @body body: Table;
  } | Problem;
}

@route("/v1/console/project/databases/{database}/tables/{table}/rows")
@tag("console")
@useAuth(consoleSession)
interface ConsoleRowsApi {
  @doc("Lists a table's rows, as `rows.list` does. Any member; the console needs no permission rules.")
  @operationId("consoleRows.list")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleRows")
  @get
  list(
    @doc("The database slug, such as `main`.")
    @path
    database: string,

    @doc("The table name.")
    @path
    table: string,

    @doc("The `nextCursor` of the previous page.")
    @query
    cursor?: string,

    @doc("Rows per page, 1 to 100. Defaults to 25.")
    @query
    limit?: int32,
  ): RowPage | Problem;
}
