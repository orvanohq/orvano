// Fails when an example's Orvano packages are not pinned to exactly the VERSION being released (spec 0011, AC-14).
// Reads every examples/*/package.json; Dart and .NET examples join as they land.
import { readdir, readFile } from 'node:fs/promises'
import { join } from 'node:path'

const version = (await readFile('VERSION', 'utf8')).trim()
const wrong = []
for (const entry of await readdir('examples', { withFileTypes: true })) {
  if (!entry.isDirectory()) continue
  const path = join('examples', entry.name, 'package.json')
  let pkg
  try {
    pkg = JSON.parse(await readFile(path, 'utf8'))
  } catch (error) {
    if (error.code === 'ENOENT') continue
    throw error
  }
  for (const deps of [pkg.dependencies ?? {}, pkg.devDependencies ?? {}]) {
    for (const [name, pin] of Object.entries(deps)) {
      if (name.startsWith('@orvano/') && pin !== version)
        wrong.push(`${path}: ${name} is ${pin}, not ${version}`)
    }
  }
}

if (wrong.length > 0) {
  for (const line of wrong) console.error(`::error::${line}`)
  process.exit(1)
}
console.log(`Every example pins its Orvano packages to ${version}.`)
