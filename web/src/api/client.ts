import createClient, { type Middleware } from 'openapi-fetch'
import { clearAccessToken, getAccessToken } from './auth'
import { apiBaseUrl } from './config'
import { ApiError, NetworkError } from './problem'
import type { paths } from './schema'

const auth: Middleware = {
  async onRequest({ request }) {
    request.headers.set('Authorization', `Bearer ${await getAccessToken()}`)
    return request
  },
  onResponse({ response }) {
    if (response.status === 401) {
      clearAccessToken()
    }
    return response
  },
}

export const api = createClient<paths>({
  baseUrl: apiBaseUrl,
  // Look fetch up on each call (not once at start-up) so test doubles installed later are used.
  fetch: (request) => globalThis.fetch(request),
})
api.use(auth)

interface FetchResult<T> {
  data?: T
  error?: unknown
  response: Response
}

/**
 * Awaits an openapi-fetch call and returns its body, or throws ApiError (the API answered with an error) or
 * NetworkError (it didn't answer). Keeps components free of `{ data, error }` branching.
 */
export async function unwrap<T>(call: Promise<FetchResult<T>>): Promise<{ data: T; response: Response }> {
  let result: FetchResult<T>
  try {
    result = await call
  } catch (cause) {
    throw cause instanceof ApiError ? cause : new NetworkError(cause)
  }
  const { data, error, response } = result
  if (!response.ok) {
    throw new ApiError(response.status, (error ?? undefined) as ConstructorParameters<typeof ApiError>[1])
  }
  return { data: data as T, response }
}
