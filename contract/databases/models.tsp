// The models of databases, tables, columns, and rows (spec 0015). A database is a Postgres schema of the project,
// a table is a real Postgres table in it, and a row is an open JSON object keyed by column name.
import "@typespec/http";
import "@typespec/openapi";

using Http;
using OpenAPI;

namespace Orvano;

@doc("Whether a database can be used yet.")
enum DatabaseStatus {
  @doc("Its schema is being created; table and row calls answer 409 `database_not_ready`.")
  provisioning: "provisioning",

  @doc("Ready to use.")
  active: "active",

  @doc("Creating its schema failed; retry it from the console.")
  failed: "failed",
}

@doc("""
  A named group of tables, backed by its own Postgres schema. Every project has `main`, which can't be renamed or
  deleted.
  """)
model Database {
  @doc("The database ID: `main` for the main database, else 20 characters of `[a-z0-9]`.")
  @example("main")
  id: string;

  @doc("The name used in paths and SDK calls: `main`, or `^[a-z][a-z0-9_]{0,39}$`.")
  @example("main")
  slug: string;

  @doc("A display name, 1 to 100 characters.")
  @example("Main")
  name: string;

  @doc("Whether the database can be used yet.")
  status: DatabaseStatus;

  @doc("True for `main`.")
  @example(true)
  main: boolean;

  @doc("When the database was created; null for `main`, which exists as long as its project.")
  createdAt: utcDateTime | null;
}

@doc("One page of a project's databases: `main` first, then by slug.")
model DatabasePage {
  @doc("The databases on this page.")
  items: Database[];

  @doc("Pass it as `cursor` to get the next page; null on the last page.")
  nextCursor: string | null;
}

@doc("""
  A column's type in the API. Each maps to one Postgres type; `other` is any type Orvano does not map, such as a
  column made in SQL.
  """)
enum ColumnType {
  @doc("`text`: a string.")
  text: "text",

  @doc("`int4`: a 32 bit integer, sent as a JSON number.")
  integer: "integer",

  @doc("`int8`: a 64 bit integer, sent as a string (a number is accepted on input).")
  bigint: "bigint",

  @doc("`float8`: a double, sent as a JSON number.")
  float: "float",

  @doc("`numeric`: an exact decimal, sent as a string (a number is accepted on input).")
  decimal: "decimal",

  @doc("`boolean`.")
  boolean: "boolean",

  @doc("`timestamptz`: ISO 8601 in UTC with microseconds on output; any ISO 8601 with an offset on input.")
  timestamp: "timestamp",

  @doc("`date`: `YYYY-MM-DD`.")
  date: "date",

  @doc("`uuid`: a lowercase string.")
  uuid: "uuid",

  @doc("`jsonb`: any JSON value.")
  json: "json",

  @doc("`text[]`: an array of strings.")
  textArray: "text[]",

  @doc("`int4[]`: an array of integers.")
  integerArray: "integer[]",

  @doc("`uuid[]`: an array of uuids.")
  uuidArray: "uuid[]",

  @doc("Any other Postgres type: read as its text form, never writable.")
  other: "other",
}

@doc("How a column's default is made.")
enum DefaultKind {
  @doc("A fixed value, in `value`.")
  value: "value",

  @doc("The current time, `now()`; timestamp and date columns only.")
  now: "now",

  @doc("A time ordered uuid, `uuidv7()`; uuid columns only.")
  uuidv7: "uuidv7",

  @doc("A random uuid, `gen_random_uuid()`; uuid columns only.")
  randomUuid: "random_uuid",

  @doc("Any other SQL expression, made outside Orvano; `value` holds its SQL text. Never accepted as input.")
  expression: "expression",
}

@doc("A column's default value.")
model ColumnDefault {
  @doc("How the default is made.")
  kind: DefaultKind;

  @doc("""
    For `value`, the value as text (arrays in Postgres array syntax such as `{a,b}`, json as JSON text); for
    `expression`, the SQL text; null otherwise.
    """)
  @example("open")
  value: string | null;
}

@doc("A column, read live from Postgres.")
model Column {
  @doc("The column name.")
  @example("title")
  name: string;

  @doc("The column's type in the API.")
  type: ColumnType;

  @doc("The Postgres type name, such as `int4` or `_text`.")
  @example("text")
  pgType: string;

  @doc("True when the column is NOT NULL.")
  @example(false)
  required: boolean;

  @doc("True when a single column unique constraint or unique index covers exactly this column.")
  @example(false)
  unique: boolean;

  @doc("The default, or null when there is none.")
  default: ColumnDefault | null;

  @doc("True for `id`, `created_at`, and `updated_at` on a standard table.")
  @example(false)
  system: boolean;

  @doc("False for system columns, `other` types, generated columns, and names the API can't address.")
  @example(true)
  writable: boolean;
}

@doc("A column to create.")
model ColumnInput {
  @doc("""
    `^[a-z][a-z0-9_]{0,62}$`, not a reserved Postgres keyword, not starting with `pg_` or `orvano`, and not `id`,
    `created_at`, or `updated_at`.
    """)
  @example("title")
  name: string;

  @doc("The column's type; `other` is refused.")
  type: ColumnType;

  @doc("True makes it NOT NULL. Defaults to false.")
  @example(false)
  required?: boolean;

  @doc("True adds a single column unique constraint. Defaults to false.")
  @example(false)
  unique?: boolean;

  @doc("The default, if any. Kind `expression` is refused.")
  default?: ColumnDefault | null;
}

@doc("A table, read live from Postgres.")
model Table {
  @doc("The table name.")
  @example("tasks")
  name: string;

  @doc("The slug of the database it is in.")
  @example("main")
  database: string;

  @doc("Every column, in order.")
  columns: Column[];

  @doc("False when the table has no primary key, so its rows can't be listed through the API.")
  @example(true)
  readable: boolean;

  @doc("True only for a table whose primary key is the standard `id uuid`; others are read only through the API.")
  @example(true)
  writable: boolean;

  @doc("Postgres' estimate of the row count; null when the table was never analyzed.")
  @example(1200)
  estimatedRows: int64 | null;
}

@doc("One page of a database's tables, by name.")
model TablePage {
  @doc("The tables on this page.")
  items: Table[];

  @doc("Pass it as `cursor` to get the next page; null on the last page.")
  nextCursor: string | null;
}

@doc("A new table. It always gets `id`, `created_at`, and `updated_at` first.")
model CreateTableRequest {
  @doc("`^[a-z][a-z0-9_]{0,62}$`, not a reserved Postgres keyword, and not starting with `pg_` or `orvano`.")
  @example("tasks")
  name: string;

  @doc("0 to 200 columns, in order.")
  columns: ColumnInput[];
}

@doc("""
  A row: a JSON object keyed by column name. Values travel as their column type says: strings for text, uuid,
  bigint, and decimal; numbers for integer and float; ISO 8601 for timestamps; `YYYY-MM-DD` for dates; any JSON for
  json; arrays for array types.
  """)
@extension("x-orvano-dynamic", true)
model Row is Record<unknown>;

@doc("One page of rows.")
model RowPage {
  @doc("The rows on this page.")
  items: Row[];

  @doc("Pass it as `cursor` to get the next page; null on the last page.")
  nextCursor: string | null;
}
