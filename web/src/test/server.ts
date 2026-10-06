import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { apiBaseUrl } from '../api/config'
import { countries, customers } from './fixtures'

export const url = (path: string) => `${apiBaseUrl}${path}`

/** A ProblemDetails body shaped like the API's (friendly title/detail, code, correlationId). */
export function problem(status: number, body: Record<string, unknown>) {
  return HttpResponse.json(
    { type: 'about:blank', status, correlationId: 'corr-123', ...body },
    { status, headers: { 'Content-Type': 'application/problem+json' } },
  )
}

// Defaults every page needs: a dev token, reference data and customer lookups. Tests add their own on top.
export const defaultHandlers = [
  http.post(url('/api/v1/dev/token'), () =>
    HttpResponse.json({ accessToken: 'test-token', tokenType: 'Bearer', expiresIn: 3600, roles: ['Orders.Read'] }),
  ),
  http.get(url('/api/v1/reference/countries'), () => HttpResponse.json(countries)),
  http.get(url('/api/v1/customers/:id'), ({ params }) => {
    const customer = customers.find((c) => c.id === params.id)
    return customer ? HttpResponse.json(customer) : problem(404, { title: "We couldn't find that", code: 'not_found' })
  }),
  http.get(url('/api/v1/customers'), ({ request }) => {
    const search = new URL(request.url).searchParams.get('search')?.toLowerCase() ?? ''
    const items = customers.filter((c) => c.name.toLowerCase().startsWith(search) || c.email.startsWith(search))
    return HttpResponse.json({
      items,
      page: 1,
      pageSize: 20,
      totalCount: items.length,
      totalPages: items.length ? 1 : 0,
      hasPrevious: false,
      hasNext: false,
    })
  }),
]

export const server = setupServer(...defaultHandlers)
