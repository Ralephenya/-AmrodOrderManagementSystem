import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { initialTheme, THEME_STORAGE_KEY, ThemeContext, type Theme } from '@/lib/theme'

/** Light/dark theme as a `dark` class on <html>, which the Tailwind `dark:` variant and the CSS tokens key off. */
export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setTheme] = useState<Theme>(initialTheme)

  useEffect(() => {
    const root = document.documentElement
    root.classList.toggle('dark', theme === 'dark')
    root.style.colorScheme = theme
  }, [theme])

  // Saved only when the user picks a theme, so until then the OS preference keeps applying.
  const toggle = useCallback(
    () =>
      setTheme((t) => {
        const next = t === 'dark' ? 'light' : 'dark'
        try {
          localStorage.setItem(THEME_STORAGE_KEY, next)
        } catch {
          // Not persisting is fine; the toggle still works for this visit.
        }
        return next
      }),
    [],
  )
  const api = useMemo(() => ({ theme, toggle }), [theme, toggle])

  return <ThemeContext.Provider value={api}>{children}</ThemeContext.Provider>
}
