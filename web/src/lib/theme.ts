import { createContext, useContext } from 'react'

export type Theme = 'light' | 'dark'

export const THEME_STORAGE_KEY = 'om-theme'

/** The saved choice, else the OS preference. index.html applies the same rule before first paint (no flash). */
export function initialTheme(): Theme {
  try {
    const saved = localStorage.getItem(THEME_STORAGE_KEY)
    if (saved === 'light' || saved === 'dark') return saved
  } catch {
    // Storage can be blocked (private mode); fall back to the OS preference.
  }
  return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

export interface ThemeApi {
  theme: Theme
  toggle(): void
}

export const ThemeContext = createContext<ThemeApi | null>(null)

export function useTheme(): ThemeApi {
  const api = useContext(ThemeContext)
  if (!api) {
    throw new Error('useTheme must be used inside <ThemeProvider>.')
  }
  return api
}
