import type { ColumnType, DefaultKind } from '@orvano/console-client'

/** A type a new column can have: every API type but `other` (spec 0015, AC-5). */
export type CreatableType = Exclude<ColumnType, 'other'>

/** The types offered in the column builder, in the order the select lists them. */
export const creatableTypes: readonly CreatableType[] = [
  'text',
  'integer',
  'bigint',
  'float',
  'decimal',
  'boolean',
  'timestamp',
  'date',
  'uuid',
  'json',
  'text[]',
  'integer[]',
  'uuid[]',
]

/** What each type stores, for the type select. */
export const typeLabels: Record<ColumnType, string> = {
  text: 'Text',
  integer: 'Integer',
  bigint: 'Big integer',
  float: 'Float',
  decimal: 'Decimal',
  boolean: 'Boolean',
  timestamp: 'Timestamp',
  date: 'Date',
  uuid: 'UUID',
  json: 'JSON',
  'text[]': 'Text list',
  'integer[]': 'Integer list',
  'uuid[]': 'UUID list',
  other: 'Other',
}

/** A default the builder offers: none, or one of the kinds a new column takes (never `expression`). */
export type DefaultChoice = 'none' | Exclude<DefaultKind, 'expression'>

export const defaultLabels: Record<DefaultChoice, string> = {
  none: 'No default',
  value: 'A fixed value',
  now: 'The current time',
  uuidv7: 'A time ordered UUID',
  random_uuid: 'A random UUID',
}

/** The defaults a type takes (AC-5): `now` for times and dates, the uuid functions for uuids. */
export function defaultChoices(type: CreatableType): DefaultChoice[] {
  if (type === 'timestamp' || type === 'date') return ['none', 'value', 'now']
  if (type === 'uuid') return ['none', 'value', 'uuidv7', 'random_uuid']
  return ['none', 'value']
}

/** How to write a fixed default for a type, as the value input's hint. */
export function valueHint(type: CreatableType): string {
  switch (type) {
    case 'boolean':
      return 'true or false'
    case 'timestamp':
      return 'For example 2026-01-01T00:00:00Z'
    case 'date':
      return 'For example 2026-01-01'
    case 'json':
      return 'JSON, for example {"a": 1}'
    case 'text[]':
    case 'integer[]':
    case 'uuid[]':
      return 'Postgres list syntax, for example {a,b}'
    default:
      return 'Postgres checks the value when you create the table'
  }
}

const namePattern = /^[a-z][a-z0-9_]{0,62}$/

/**
 * The name rules the console can check before sending (AC-5): the pattern, the reserved prefixes,
 * and the system column names. Reserved Postgres words are left to the server, which knows them.
 */
export function nameProblem(name: string, column: boolean): string | undefined {
  if (name === '') return 'Enter a name'
  if (!namePattern.test(name)) {
    return 'Use 1 to 63 characters of a to z, 0 to 9, and _, starting with a letter'
  }
  if (name.startsWith('pg_') || name.startsWith('orvano')) {
    return "Names can't start with pg_ or orvano"
  }
  if (column && (name === 'id' || name === 'created_at' || name === 'updated_at')) {
    return 'Every table already has this column'
  }
  return undefined
}

/**
 * A server problem about one field of the create request (`columns[2].name: ...`), or undefined.
 * The console shows it under that field; anything else goes in the form alert.
 */
export function fieldOfProblem(
  detail: string,
): { column: number | null; field: 'name' | 'type' | 'default'; message: string } | undefined {
  const match = /^(?:columns\[(\d+)\]\.)?(name|type|default(?:\.kind|\.value)?): (.+)$/.exec(detail)
  if (match === null) return undefined
  const index = match.at(1)
  const path = match.at(2) ?? ''
  const message = match.at(3) ?? ''
  const field = path.startsWith('default') ? 'default' : (path as 'name' | 'type')
  const sentence = message.charAt(0).toUpperCase() + message.slice(1)
  return { column: index === undefined ? null : Number(index), field, message: sentence }
}
