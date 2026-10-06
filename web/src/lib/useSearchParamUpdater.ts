import { useCallback } from 'react'
import { useSearchParams } from 'react-router'

/**
 * Sets or clears query-string values (undefined or '' clears), keeping the rest of the query string.
 * `replace` updates the current history entry, for values that change on every keystroke.
 */
export function useSearchParamUpdater() {
  const [, setParams] = useSearchParams()
  return useCallback(
    (changes: Record<string, string | undefined>, options: { replace?: boolean } = {}) =>
      setParams((current) => {
        const next = new URLSearchParams(current)
        for (const [key, value] of Object.entries(changes)) {
          if (value) next.set(key, value)
          else next.delete(key)
        }
        return next
      }, options),
    [setParams],
  )
}
