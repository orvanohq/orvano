// Checks that a quickstart refuses to start without a setting (spec 0011, AC-12): the command must exit with an
// error within the time limit, and its output must name what's missing. A server that starts listening anyway runs
// past the limit and fails the check. The caller removes the setting, for example with `env -u ORVANO_API_KEY`.
//
// Usage: node expect-refusal.mjs <seconds> <expected message> -- <command> [args...]
import { spawn } from 'node:child_process'

const [seconds, expected, separator, command, ...args] = process.argv.slice(2)
if (
  seconds === undefined ||
  expected === undefined ||
  separator !== '--' ||
  command === undefined
) {
  throw new Error(
    'Usage: node expect-refusal.mjs <seconds> <expected message> -- <command> [args...]',
  )
}

const child = spawn(command, args, { stdio: ['ignore', 'pipe', 'pipe'] })
let output = ''
child.stdout.on('data', (chunk) => (output += chunk))
child.stderr.on('data', (chunk) => (output += chunk))

const shown = `\`${[command, ...args].join(' ')}\``
const timer = setTimeout(
  () => {
    child.kill('SIGKILL')
    fail(
      `${shown} was still running after ${seconds}s; it should refuse to start and name: ${expected}`,
    )
  },
  Number(seconds) * 1000,
)

child.on('error', (error) => fail(`${shown} could not run: ${error.message}`))
child.on('close', (code) => {
  clearTimeout(timer)
  if (code === 0) fail(`${shown} exited 0; it should refuse to start and name: ${expected}`)
  if (!output.includes(expected)) fail(`${shown} exited ${code} without saying: ${expected}`)
  console.log(`${shown} refused to start (exit ${code}): ${expected}`)
})

function fail(message) {
  console.log(output)
  console.log(`::error::${message}`)
  process.exit(1)
}
