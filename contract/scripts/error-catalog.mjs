// Writes dist/errors.json, the error catalog the docs site turns into a page per code (spec 0011, AC-16).
// Runs after `tsp compile`: it compiles the contract again without emitting and reads `enum ErrorCode`.
// Every member's @doc must end with one HTTP status in parentheses, for example "No such user in the project (404).".
// `TestErrorCode` and every member whose @doc starts with "`Test` environment only" stay out.
import { compile, getDoc, NodeHost } from '@typespec/compiler'
import { writeFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'

const root = fileURLToPath(new URL('..', import.meta.url))
const program = await compile(NodeHost, `${root}main.tsp`, { noEmit: true })
if (program.hasError()) throw new Error('The contract did not compile.')

const [errorCode] = program.resolveTypeReference('Orvano.ErrorCode')
if (errorCode?.kind !== 'Enum') throw new Error('The contract has no enum Orvano.ErrorCode.')

const trailingStatus = /^(.*\S)\s*\((\d{3})\)\.$/s
const errors = []
const malformed = []
for (const member of errorCode.members.values()) {
  const doc = (getDoc(program, member) ?? '').trim()
  if (doc.startsWith('`Test` environment only')) continue
  const match = trailingStatus.exec(doc)
  if (match === null) {
    malformed.push(member.name)
    continue
  }
  errors.push({ code: member.name, status: Number(match[2]), description: `${match[1]}.` })
}

if (malformed.length > 0) {
  throw new Error(
    `These ErrorCode members need a @doc ending with one HTTP status in parentheses, like "(404).": ${malformed.join(', ')}`,
  )
}

await writeFile(`${root}dist/errors.json`, `${JSON.stringify(errors, null, 2)}\n`)
console.log(`dist/errors.json lists ${errors.length} error codes.`)
