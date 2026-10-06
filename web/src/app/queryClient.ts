import { QueryClient } from '@tanstack/react-query'
import { ApiError, NetworkError } from '../api/problem'

/** Retry only what a retry can fix: network failures and 5xx/429. A 4xx won't change by asking again. */
function shouldRetry(failureCount: number, error: unknown): boolean {
  if (failureCount >= 2) return false
  if (error instanceof NetworkError) return true
  return error instanceof ApiError && (error.status >= 500 || error.status === 429)
}

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: shouldRetry, staleTime: 15_000, refetchOnWindowFocus: true },
      // Creates aren't idempotent, so mutations don't retry by default. Status changes opt in (see queries.ts).
      mutations: { retry: false },
    },
  })
}
