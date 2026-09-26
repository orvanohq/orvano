import {
  createContext,
  use,
  useCallback,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react'

/** The theme choice; `system` follows the operating system. */
export type ThemeChoice = 'dark' | 'light' | 'system'
/** The density choice. */
export type Density = 'compact' | 'comfortable'
type ResolvedTheme = 'dark' | 'light'

const themeKey = 'orvano.theme'
const densityKey = 'orvano.density'
const darkQuery = '(prefers-color-scheme: dark)'

function read<T extends string>(key: string, allowed: readonly T[], fallback: T): T {
  try {
    const value = window.localStorage.getItem(key)
    return allowed.find((option) => option === value) ?? fallback
  } catch {
    return fallback
  }
}

function write(key: string, value: string): void {
  try {
    window.localStorage.setItem(key, value)
  } catch {
    // Storage can be blocked; the choice then lasts for this visit only.
  }
}

interface Preferences {
  theme: ThemeChoice
  density: Density
  setTheme: (theme: ThemeChoice) => void
  setDensity: (density: Density) => void
}

const PreferencesContext = createContext<Preferences | null>(null)

function applyTheme(resolved: ResolvedTheme): void {
  const root = document.documentElement
  root.dataset.theme = resolved
  root.style.colorScheme = resolved
}

/**
 * Owns the theme and density after start-up. `theme-init.js` already resolved both before first
 * paint, so this reads the resolved values from `<html>` and never resolves System a second time
 * at mount; it then saves changes and follows the operating system while the choice is System.
 */
export function PreferencesProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<ThemeChoice>(() =>
    read(themeKey, ['dark', 'light', 'system'], 'dark'),
  )
  const [density, setDensityState] = useState<Density>(() =>
    read(densityKey, ['compact', 'comfortable'], 'compact'),
  )

  const setTheme = useCallback((next: ThemeChoice) => {
    setThemeState(next)
    write(themeKey, next)
    applyTheme(next === 'system' ? (window.matchMedia(darkQuery).matches ? 'dark' : 'light') : next)
  }, [])

  const setDensity = useCallback((next: Density) => {
    setDensityState(next)
    write(densityKey, next)
    document.documentElement.dataset.density = next
  }, [])

  useEffect(() => {
    if (theme !== 'system') return
    const media = window.matchMedia(darkQuery)
    const onChange = (event: MediaQueryListEvent) => {
      applyTheme(event.matches ? 'dark' : 'light')
    }
    media.addEventListener('change', onChange)
    return () => {
      media.removeEventListener('change', onChange)
    }
  }, [theme])

  const value = useMemo(
    () => ({ theme, density, setTheme, setDensity }),
    [theme, density, setTheme, setDensity],
  )
  return <PreferencesContext value={value}>{children}</PreferencesContext>
}

/** The current theme and density and their setters. */
export function usePreferences(): Preferences {
  const value = use(PreferencesContext)
  if (value === null) throw new Error('usePreferences needs a PreferencesProvider')
  return value
}
