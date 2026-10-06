import type { ProblemDetails } from './types'

/**
 * An error response from the API, carrying its RFC 7807 ProblemDetails. The API writes a friendly `title` and
 * `detail`, a stable `code` and a `correlationId` on every error, so the UI shows those rather than inventing text.
 */
export class ApiError extends Error {
  readonly status: number
  readonly title: string
  readonly detail: string | undefined
  readonly code: string | undefined
  readonly correlationId: string | undefined
  /** Field errors keyed by form path (`lineItems.0.quantity`), from a 400 ValidationProblemDetails. */
  readonly fieldErrors: Readonly<Record<string, string>>

  constructor(status: number, problem: ProblemDetails | undefined) {
    const title = problem?.title ?? fallbackTitle(status)
    super(title)
    this.name = 'ApiError'
    this.status = status
    this.title = title
    this.detail = problem?.detail ?? undefined
    this.code = typeof problem?.code === 'string' ? problem.code : undefined
    this.correlationId = typeof problem?.correlationId === 'string' ? problem.correlationId : undefined
    this.fieldErrors = toFieldErrors(problem?.errors)
  }
}

/** Thrown when the API can't be reached at all (offline, CORS, server down). */
export class NetworkError extends Error {
  constructor(cause: unknown) {
    super("We couldn't reach the server. Check your connection and try again.", { cause })
    this.name = 'NetworkError'
  }
}

/** Plain-language summary of any error, for toasts and alerts. */
export function describeError(error: unknown): { title: string; detail?: string; reference?: string } {
  if (error instanceof ApiError) {
    return { title: error.title, detail: error.detail, reference: error.correlationId }
  }
  if (error instanceof NetworkError) {
    return { title: error.message }
  }
  return { title: 'Something went wrong', detail: 'Please try again.' }
}

function toFieldErrors(errors: unknown): Record<string, string> {
  if (errors === null || typeof errors !== 'object') {
    return {}
  }
  const result: Record<string, string> = {}
  for (const [key, messages] of Object.entries(errors)) {
    const first = Array.isArray(messages) ? messages[0] : undefined
    if (typeof first === 'string') {
      // "lineItems[0].quantity" (the API's path) -> "lineItems.0.quantity" (react-hook-form's path)
      result[key.replace(/\[(\d+)\]/g, '.$1')] = first
    }
  }
  return result
}

function fallbackTitle(status: number): string {
  if (status === 401) return 'You need to sign in'
  if (status === 403) return "You don't have access to this"
  if (status === 404) return "We couldn't find that"
  if (status >= 500) return 'Something went wrong on our side'
  return 'That request failed'
}
