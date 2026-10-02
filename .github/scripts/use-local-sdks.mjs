// Points a copied example at the SDKs of this checkout (spec 0011, AC-19), so the quickstarts jobs test the SDKs of
// the pull request, not the ones on npm, pub.dev, or NuGet. The committed example keeps its exact version pins:
//
// - package.json: `@orvano/*` become `file:` tarballs from `pnpm pack` in the packs folder, plus `overrides`.
// - pubspec.yaml: a `pubspec_overrides.yaml` points `orvano_*` at sdks/dart/ in this checkout.
// - *.csproj: a `nuget.config` adds the packs folder (the `.nupkg` from `dotnet pack`) as a package source.
//
// Usage: node use-local-sdks.mjs <example copy> <packs folder>
import { existsSync } from 'node:fs'
import { readdir, readFile, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'

const [example, packsArg] = process.argv.slice(2)
if (example === undefined || packsArg === undefined) {
  throw new Error('Usage: node use-local-sdks.mjs <example copy> <packs folder>')
}
const packs = resolve(packsArg)
const repo = resolve(import.meta.dirname, '../..')

if (existsSync(join(example, 'package.json'))) {
  // `orvano-nextjs-0.0.0.tgz` is the tarball of `@orvano/nextjs`.
  const tarballs = new Map()
  for (const file of await readdir(packs)) {
    const match = /^orvano-(.+)-\d+\.\d+\.\d+\.tgz$/.exec(file)
    if (match !== null) tarballs.set(`@orvano/${match[1]}`, `file:${join(packs, file)}`)
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
}

if (existsSync(join(example, 'pubspec.yaml'))) {
  // Overrides for packages outside the app's graph are ignored, so one file fits the Dart and Flutter examples.
  const packages = { orvano_core: 'core', orvano_dart: 'server', orvano_flutter: 'flutter' }
  const lines = ['dependency_overrides:']
  for (const [name, folder] of Object.entries(packages)) {
    lines.push(`  ${name}:`, `    path: ${join(repo, 'sdks/dart', folder)}`)
  }
  await writeFile(join(example, 'pubspec_overrides.yaml'), `${lines.join('\n')}\n`)
  console.log(
    `${example}/pubspec_overrides.yaml now points orvano_* at ${join(repo, 'sdks/dart')}.`,
  )
}

if ((await readdir(example)).some((file) => file.endsWith('.csproj'))) {
  await writeFile(
    join(example, 'nuget.config'),
    [
      '<?xml version="1.0" encoding="utf-8"?>',
      '<configuration>',
      '  <packageSources>',
      `    <add key="checkout" value="${packs}" />`,
      '    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />',
      '  </packageSources>',
      '  <!-- Orvano comes only from the checkout, even if nuget.org ever has the same version. -->',
      '  <packageSourceMapping>',
      '    <packageSource key="checkout"><package pattern="Orvano" /></packageSource>',
      '    <packageSource key="nuget.org"><package pattern="*" /></packageSource>',
      '  </packageSourceMapping>',
      '</configuration>',
      '',
    ].join('\n'),
  )
  console.log(`${example}/nuget.config now reads packages from ${packs} first.`)
}
