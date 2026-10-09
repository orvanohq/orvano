import { OrvanoError } from '@orvano/console-client'

/** A refused save, placed on the field it names. */
export interface PlacedError {
  field: string
  message: string
}

/** The entries of a one per line textarea, with the line (1 based) each came from. */
export interface Lines {
  entries: string[]
  lineOf: number[]
}

const LIST_ENTRIES = /^(\w+) has bad entries at positions ([\d, ]+): (.*)$/

/**
 * Places a 400 `invalid_request` from `consoleAuthPolicies.update` on the field its detail names
 * (spec 0014, AC-1, AC-34). A list's detail carries zero based positions in the list sent, which
 * `lines` turns back into the textarea's line numbers: `Line 2: ...`, `Lines 1 and 3: ...`. Any
 * other error, or a field this card doesn't hold, returns `null` for the card's alert.
 */
export function placePolicyError(
  error: unknown,
  fields: readonly string[],
  lines: Readonly<Partial<Record<string, Lines>>> = {},
): PlacedError | null {
  if (!(error instanceof OrvanoError) || error.status !== 400 || error.code !== 'invalid_request')
    return null
  const detail = error.message
  const list = LIST_ENTRIES.exec(detail)
  if (list !== null) {
    const [, field = '', positions = '', reason = ''] = list
    if (!fields.includes(field)) return null
    const lineOf = lines[field]?.lineOf ?? []
    const numbers = positions
      .split(',')
      .map((position) => Number(position.trim()))
      .filter((position) => Number.isInteger(position))
      .map((position) => lineOf[position] ?? position + 1)
    return { field, message: `${linesLabel(numbers)}: ${capitalize(reason)}` }
  }
  const field = fields.find(
    (name) => detail.startsWith(`${name} `) || detail.startsWith(`${name}.`),
  )
  return field === undefined ? null : { field, message: detail }
}

/** `Line 2`, `Lines 1 and 3`, `Lines 1, 2, and 5`. */
export function linesLabel(lines: readonly number[]): string {
  const text = lines.map(String)
  if (text.length <= 1) return `Line ${text[0] ?? ''}`
  if (text.length === 2) return `Lines ${text[0] ?? ''} and ${text[1] ?? ''}`
  return `Lines ${text.slice(0, -1).join(', ')}, and ${text[text.length - 1] ?? ''}`
}

function capitalize(text: string): string {
  return text.charAt(0).toUpperCase() + text.slice(1)
}

/**
 * Reads a whole number typed into a policy field, or `null` when it isn't one from `min` to `max`
 * (the bounds the server checks too).
 */
export function readWholeNumber(text: string, min: number, max: number): number | null {
  const trimmed = text.trim()
  if (!/^\d+$/.test(trimmed)) return null
  const value = Number(trimmed)
  return value >= min && value <= max ? value : null
}

/** One entry per non blank line, trimmed, remembering each entry's line for error messages. */
export function readLines(text: string): Lines {
  const entries: string[] = []
  const lineOf: number[] = []
  text.split('\n').forEach((line, index) => {
    const trimmed = line.trim()
    if (trimmed === '') return
    entries.push(trimmed)
    lineOf.push(index + 1)
  })
  return { entries, lineOf }
}
