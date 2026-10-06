import { apiBaseUrl } from './config'
import { ApiError } from './problem'

export const APP_ROLES = ['Orders.Read', 'Orders.Write', 'Orders.Admin'] as const

interface CachedToken {
  value: string
  expiresAt: number
}

// Refresh this long before the token actually expires, so a request never leaves with a token about to lapse.
const REFRESH_MARGIN_MS = 60_000

let cached: CachedToken | null = null
let pending: Promise<string> | null = null

/**
 * Returns a bearer token for the API. In Development the API runs in mock-Entra mode and mints tokens from
 * `POST /api/v1/dev/token`; this asks for all three app roles. Swapping in MSAL for real Entra sign-in only
 * replaces this function.
 */
export function getAccessToken(): Promise<string> {
  if (cached && cached.expiresAt - REFRESH_MARGIN_MS > Date.now()) {
    return Promise.resolve(cached.value)
  }
  pending ??= requestDevToken().finally(() => {
    pending = null
  })
  return pending
}

/** Forget the cached token, e.g. after a 401, so the next call fetches a fresh one. */
export function clearAccessToken(): void {
  cached = null
}

async function requestDevToken(): Promise<string> {
  const response = await fetch(`${apiBaseUrl}/api/v1/dev/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name: 'Web Developer', roles: APP_ROLES }),
  })
  if (!response.ok) {
    // 404 means the API isn't in mock-auth Development mode, so there's no dev token endpoint to call.
    throw new ApiError(response.status, {
      title: "Couldn't sign you in",
      detail: 'The API did not issue a development token. Check it is running in Development with Auth:Mode set to Mock.',
    })
  }
  const body = (await response.json()) as { accessToken: string; expiresIn: number }
  cached = { value: body.accessToken, expiresAt: Date.now() + body.expiresIn * 1000 }
  return body.accessToken
}
