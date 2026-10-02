// Fails when an example's Orvano packages are not pinned to exactly the VERSION being released (spec 0011, AC-14):
// `@orvano/*` in every examples/*/package.json, `orvano_*` in every examples/*/pubspec.yaml, and the `Orvano` package
// in every examples/*/*.csproj. It also checks that the Flutter example's iOS bundle ID and Android package name are
// the app ID the console journey registers (AC-10).
import { readdir, readFile } from 'node:fs/promises'
import { join } from 'node:path'

const version = (await readFile('VERSION', 'utf8')).trim()
const wrong = []

/** The file's text, or null when the example has no such file. */
async function read(path) {
  try {
    return await readFile(path, 'utf8')
  } catch (error) {
    if (error.code === 'ENOENT') return null
    throw error
  }
}

for (const entry of await readdir('examples', { withFileTypes: true })) {
  if (!entry.isDirectory()) continue
  const dir = join('examples', entry.name)

  const packageJson = await read(join(dir, 'package.json'))
  if (packageJson !== null) {
    const pkg = JSON.parse(packageJson)
    for (const deps of [pkg.dependencies ?? {}, pkg.devDependencies ?? {}]) {
      for (const [name, pin] of Object.entries(deps)) {
        if (name.startsWith('@orvano/') && pin !== version)
          wrong.push(`${dir}/package.json: ${name} is ${pin}, not ${version}`)
      }
    }
  }

  const pubspec = await read(join(dir, 'pubspec.yaml'))
  if (pubspec !== null) {
    for (const [, name, pin] of pubspec.matchAll(/^\s+(orvano_[a-z_]+):\s*(\S+)\s*$/gm)) {
      if (pin !== version) wrong.push(`${dir}/pubspec.yaml: ${name} is ${pin}, not ${version}`)
    }
  }

  for (const file of (await readdir(dir)).filter((name) => name.endsWith('.csproj'))) {
    const project = await read(join(dir, file))
    for (const [, pin] of project.matchAll(
      /<PackageReference Include="Orvano" Version="([^"]*)"/g,
    )) {
      if (pin !== version) wrong.push(`${dir}/${file}: Orvano is ${pin}, not ${version}`)
    }
  }
}

// AC-10: the Flutter app registers as the same iOS bundle ID and Android package name the console journey adds.
const journey = await readFile('website/scripts/screenshots.ts', 'utf8')
const appId = /const appId = '([^']+)'/.exec(journey)?.[1]
const flutter = 'examples/flutter-quickstart'
const ios = await read(join(flutter, 'ios/Runner.xcodeproj/project.pbxproj'))
const android = await read(join(flutter, 'android/app/build.gradle.kts'))
if (ios !== null && android !== null) {
  const bundleIds = new Set(
    [...ios.matchAll(/PRODUCT_BUNDLE_IDENTIFIER = ([\w.]+);/g)].map((m) => m[1]),
  )
  bundleIds.delete(`${appId}.RunnerTests`)
  if (bundleIds.size !== 1 || !bundleIds.has(appId))
    wrong.push(`${flutter}: the iOS bundle ID is ${[...bundleIds].join(', ')}, not ${appId}`)
  const applicationId = /applicationId = "([^"]+)"/.exec(android)?.[1]
  if (applicationId !== appId)
    wrong.push(`${flutter}: the Android applicationId is ${applicationId}, not ${appId}`)
}

if (wrong.length > 0) {
  for (const line of wrong) console.error(`::error::${line}`)
  process.exit(1)
}
console.log(
  `Every example pins its Orvano packages to ${version}, and the Flutter app ID is ${appId}.`,
)
