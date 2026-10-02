// Points a copied JS example at SDK tarballs packed from this checkout (spec 0011, AC-19), so the quickstarts job
// tests the SDKs of the pull request, not the ones on npm. The committed example keeps its exact version pins.
//
// Usage: node use-local-sdks.mjs <example copy> <folder of .tgz files from `pnpm pack`>
import { readdir, readFile, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'

const [example, packs] = process.argv.slice(2)
if (example === undefined || packs === undefined) {
  throw new Error('Usage: node use-local-sdks.mjs <example copy> <packs folder>')
}

// `orvano-nextjs-0.0.0.tgz` is the tarball of `@orvano/nextjs`.
const tarballs = new Map()
for (const file of await readdir(packs)) {
  const match = /^orvano-(.+)-\d+\.\d+\.\d+\.tgz$/.exec(file)
  if (match !== null) tarballs.set(`@orvano/${match[1]}`, `file:${resolve(packs, file)}`)
}

const path = join(example, 'package.json')
const pkg = JSON.parse(await readFile(path, 'utf8'))
for (const [name, tarball] of tarballs) {
  if (pkg.dependencies?.[name] !== undefined) pkg.dependencies[name] = tarball
}
// A packed SDK depends on its siblings by version; overrides send those to the tarballs too.
pkg.overrides = Object.fromEntries(tarballs)
await writeFile(path, `${JSON.stringify(pkg, null, 2)}\n`)
console.log(`${path} now uses ${[...tarballs.keys()].join(', ')} from ${packs}.`)
