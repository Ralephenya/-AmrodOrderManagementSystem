import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { problem, server, url } from '../test/server'
import { api, unwrap } from './client'
import { ApiError, NetworkError } from './problem'

describe('API client', () => {
  it('fetches one dev token and sends it on every request', async () => {
    let tokenRequests = 0
    const authHeaders: (string | null)[] = []
    server.use(
      http.post(url('/api/v1/dev/token'), () => {
        tokenRequests++
        return HttpResponse.json({ accessToken: 'abc', tokenType: 'Bearer', expiresIn: 3600, roles: [] })
      }),
      http.get(url('/api/v1/reference/countries'), ({ request }) => {
        authHeaders.push(request.headers.get('Authorization'))
        return HttpResponse.json([])
      }),
    )

    await Promise.all([
      unwrap(api.GET('/api/v1/reference/countries')),
      unwrap(api.GET('/api/v1/reference/countries')),
    ])
    await unwrap(api.GET('/api/v1/reference/countries'))

    expect(tokenRequests).toBe(1)
    expect(authHeaders).toEqual(['Bearer abc', 'Bearer abc', 'Bearer abc'])
  })

  it('gets a fresh token after a 401', async () => {
    let issued = 0
    server.use(
      http.post(url('/api/v1/dev/token'), () =>
        HttpResponse.json({ accessToken: `t${++issued}`, tokenType: 'Bearer', expiresIn: 3600, roles: [] }),
      ),
      http.get(url('/api/v1/reference/countries'), ({ request }) =>
        request.headers.get('Authorization') === 'Bearer t1' ? problem(401, { title: 'You need to sign in' }) : HttpResponse.json([]),
      ),
    )

    await expect(unwrap(api.GET('/api/v1/reference/countries'))).rejects.toMatchObject({ status: 401 })
    await expect(unwrap(api.GET('/api/v1/reference/countries'))).resolves.toBeDefined()
    expect(issued).toBe(2)
  })

  it('turns error responses into ApiError', async () => {
    server.use(http.get(url('/api/v1/reference/countries'), () => problem(503, { title: 'The service is temporarily unavailable' })))

    const error = await unwrap(api.GET('/api/v1/reference/countries')).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({ status: 503, title: 'The service is temporarily unavailable', correlationId: 'corr-123' })
  })

  it('turns an unreachable API into NetworkError', async () => {
    server.use(http.get(url('/api/v1/reference/countries'), () => HttpResponse.error()))

    await expect(unwrap(api.GET('/api/v1/reference/countries'))).rejects.toBeInstanceOf(NetworkError)
  })

  it("explains a missing dev token endpoint (API not in Development) instead of a network error", async () => {
    server.use(http.post(url('/api/v1/dev/token'), () => new HttpResponse(null, { status: 404 })))

    await expect(unwrap(api.GET('/api/v1/reference/countries'))).rejects.toMatchObject({
      title: "Couldn't sign you in",
    })
  })
})
