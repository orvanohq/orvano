import { clsx, type ClassValue } from 'clsx'
import { extendTailwindMerge } from 'tailwind-merge'

// The type roles in tokens.css (`text-body`, `text-small`, ...) are font sizes. Without this,
// tailwind-merge reads them as colors and drops a real text color that comes before them.
const twMerge = extendTailwindMerge({
  extend: {
    classGroups: {
      'font-size': [{ text: ['body', 'small', 'mono', 'h3'] }],
    },
  },
})

/** Joins class names and resolves Tailwind conflicts so the last one wins. */
export function cn(...inputs: ClassValue[]): string {
  return twMerge(clsx(inputs))
}
