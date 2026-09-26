import { playwright } from '@vitest/browser-playwright'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vitest/config'

const alias = { '@': fileURLToPath(new URL('./src', import.meta.url)) }

export default defineConfig({
  test: {
    projects: [
      {
        // Pure logic: the contrast and color literal checks, redirect and sort helpers.
        resolve: { alias },
        test: { name: 'unit', environment: 'node', include: ['src/**/*.unit.test.ts'] },
      },
      {
        // Components in real Chromium, with axe and the keyboard scripts (spec 0005).
        plugins: [react(), tailwindcss()],
        resolve: { alias },
        test: {
          name: 'browser',
          include: ['src/**/*.browser.test.tsx'],
          setupFiles: ['./src/test/setup.ts'],
          browser: {
            enabled: true,
            headless: true,
            provider: playwright({
              contextOptions: { permissions: ['clipboard-read', 'clipboard-write'] },
            }),
            instances: [{ browser: 'chromium' }],
          },
        },
      },
    ],
  },
})
