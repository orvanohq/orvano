import { page } from 'vitest/browser'
import '@/index.css'

// A desktop viewport: below 1024 px the sidebar is a drawer, and Chromium's default here is a phone.
await page.viewport(1280, 800)
