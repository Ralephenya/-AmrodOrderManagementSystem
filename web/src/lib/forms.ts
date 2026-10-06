import type { FieldValues, Path, UseFormSetError } from 'react-hook-form'
import { ApiError } from '../api/problem'

/**
 * Puts a 400 response's field errors onto the matching form fields. Returns true when at least one was shown,
 * so the caller only falls back to a toast for errors no field can display.
 *
 * `aliases` renames an API path to a form path, e.g. `{ lineItems: 'lineItems.root' }` for an error about a
 * field array as a whole.
 */
export function applyServerErrors<T extends FieldValues>(
  error: unknown,
  setError: UseFormSetError<T>,
  knownFields: readonly string[],
  aliases: Readonly<Record<string, string>> = {},
): boolean {
  if (!(error instanceof ApiError)) {
    return false
  }
  let shown = false
  for (const [apiPath, message] of Object.entries(error.fieldErrors)) {
    const path = aliases[apiPath] ?? apiPath
    // A known field, or a path under one ("lineItems.0.quantity" under "lineItems").
    if (knownFields.some((f) => path === f || path.startsWith(`${f}.`))) {
      setError(path as Path<T>, { type: 'server', message })
      shown = true
    }
  }
  return shown
}
