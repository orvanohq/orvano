// Bundles the browser and workerd entries after tsc, so neither needs a module resolver.
import { build } from 'esbuild'

await build({
  entryPoints: { browser: 'dist/entries/browser.js' },
  outdir: 'dist/bundles',
  bundle: true,
  format: 'esm',
  platform: 'browser',
  target: 'es2022',
})

await build({
  entryPoints: { worker: 'dist/entries/worker.js' },
  outdir: 'dist/bundles',
  bundle: true,
  format: 'esm',
  platform: 'neutral',
  target: 'es2022',
  conditions: ['workerd', 'worker', 'browser'],
})
