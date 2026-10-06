import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import type { Order } from '../../api/types'
import { pendingOrder } from '../../test/fixtures'
import { renderRoute } from '../../test/render'
import { problem, server, url } from '../../test/server'

const orderUrl = (o: Order) => url(`/api/v1/orders/${o.id}`)
const statusUrl = (o: Order) => url(`/api/v1/orders/${o.id}/status`)

interface StatusRequest {
  status: string
  idempotencyKey: string | null
  ifMatch: string | null
}

/** Serves the order, accepts status changes and records what each change request sent. */
function serveOrder(initial: Order, after: (status: string) => Order) {
  let current = initial
  let version = 1
  const requests: StatusRequest[] = []
  server.use(
    http.get(orderUrl(initial), () => HttpResponse.json(current, { headers: { ETag: `"v${version}"` } })),
    http.put(statusUrl(initial), async ({ request }) => {
      const { status } = (await request.json()) as { status: string }
      requests.push({
        status,
        idempotencyKey: request.headers.get('Idempotency-Key'),
        ifMatch: request.headers.get('If-Match'),
      })
      current = after(status)
      version++
      return HttpResponse.json(current, { headers: { ETag: `"v${version}"` } })
    }),
  )
  return requests
}

describe('Order details', () => {
  it('shows the line items and total', async () => {
    const order = pendingOrder()
    serveOrder(order, () => order)
    renderRoute(`/orders/${order.id}`)

    const lines = await screen.findByRole('table')
    expect(within(lines).getByRole('cell', { name: 'MUG-01' })).toBeInTheDocument()
    expect(within(lines).getAllByRole('row').at(-1)!.textContent!.replace(/\s/g, ' ')).toContain('ZAR 1 249,50')
    expect(await screen.findByRole('link', { name: 'Thandi Mokoena' })).toBeInTheDocument()
  })

  it('offers only the transitions the API allows', async () => {
    const order = pendingOrder()
    serveOrder(order, () => order)
    renderRoute(`/orders/${order.id}`)

    expect(await screen.findByRole('button', { name: 'Mark as paid' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Cancel order' })).toBeEnabled()
    expect(screen.queryByRole('button', { name: 'Mark as fulfilled' })).not.toBeInTheDocument()
  })

  it('sends a new Idempotency-Key per click and the ETag it showed as If-Match', async () => {
    const order = pendingOrder()
    const requests = serveOrder(order, (status) =>
      status === 'Paid'
        ? pendingOrder({ status: 'Paid', allocatedAt: '2026-10-05T12:01:00Z', allowedTransitions: ['Fulfilled', 'Cancelled'] })
        : pendingOrder({ status: 'Fulfilled', allocatedAt: '2026-10-05T12:01:00Z', allowedTransitions: [] }),
    )
    const { user } = renderRoute(`/orders/${order.id}`)

    await user.click(await screen.findByRole('button', { name: 'Mark as paid' }))
    expect(await screen.findByText('Order is now paid.')).toBeInTheDocument()
    await user.click(await screen.findByRole('button', { name: 'Mark as fulfilled' }))

    expect(await screen.findByText('Order is now fulfilled.')).toBeInTheDocument()
    expect(screen.getByText("This order is fulfilled and can't change any further.")).toBeInTheDocument()
    expect(requests.map((r) => [r.status, r.ifMatch])).toEqual([
      ['Paid', '"v1"'],
      ['Fulfilled', '"v2"'],
    ])
    expect(requests[0]!.idempotencyKey).toMatch(/^[0-9a-f-]{36}$/)
    expect(requests[1]!.idempotencyKey).not.toBe(requests[0]!.idempotencyKey)
  })

  it('asks before cancelling', async () => {
    const order = pendingOrder()
    const requests = serveOrder(order, () => pendingOrder({ status: 'Cancelled', allowedTransitions: [] }))
    const { user } = renderRoute(`/orders/${order.id}`)

    await user.click(await screen.findByRole('button', { name: 'Cancel order' }))
    expect(requests).toHaveLength(0)
    await user.click(screen.getByRole('button', { name: 'Keep it' }))
    expect(requests).toHaveLength(0)

    await user.click(screen.getByRole('button', { name: 'Cancel order' }))
    await user.click(screen.getByRole('button', { name: 'Yes, cancel it' }))

    expect(await screen.findByText('Order is now cancelled.')).toBeInTheDocument()
    expect(requests.map((r) => r.status)).toEqual(['Cancelled'])
  })

  it('waits for stock allocation before offering fulfilment, then updates by itself', async () => {
    let allocated = false
    const paid = pendingOrder({ status: 'Paid', allowedTransitions: ['Fulfilled', 'Cancelled'] })
    server.use(
      http.get(orderUrl(paid), () =>
        HttpResponse.json(allocated ? { ...paid, allocatedAt: '2026-10-05T12:01:00Z' } : paid, { headers: { ETag: '"v1"' } }),
      ),
    )
    renderRoute(`/orders/${paid.id}`)

    const fulfil = await screen.findByRole('button', { name: 'Mark as fulfilled' })
    expect(fulfil).toBeDisabled()
    expect(fulfil).toHaveAccessibleDescription(/Waiting for stock to be allocated/)

    allocated = true // the worker allocates; the page polls and picks it up
    await waitFor(() => expect(screen.getByRole('button', { name: 'Mark as fulfilled' })).toBeEnabled(), { timeout: 5000 })
    expect(screen.getByText(/^Allocated /)).toBeInTheDocument()
  })

  it('keeps checking a paid, allocated order until the worker fulfils it', async () => {
    let fulfilled = false
    const allocated = pendingOrder({
      status: 'Paid',
      allocatedAt: '2026-10-05T12:01:00Z',
      allowedTransitions: ['Fulfilled', 'Cancelled'],
    })
    server.use(
      http.get(orderUrl(allocated), () =>
        HttpResponse.json(fulfilled ? { ...allocated, status: 'Fulfilled', allowedTransitions: [] } : allocated, {
          headers: { ETag: fulfilled ? '"v2"' : '"v1"' },
        }),
      ),
    )
    renderRoute(`/orders/${allocated.id}`)
    expect(await screen.findByRole('button', { name: 'Mark as fulfilled' })).toBeEnabled()

    fulfilled = true // OrderPaidConsumer fulfils it in the background
    expect(
      await screen.findByText("This order is fulfilled and can't change any further.", {}, { timeout: 5000 }),
    ).toBeInTheDocument()
  })

  it('explains a 412 (changed elsewhere) and reloads the order', async () => {
    const order = pendingOrder()
    let loads = 0
    server.use(
      http.get(orderUrl(order), () => {
        loads++
        return HttpResponse.json(loads === 1 ? order : pendingOrder({ status: 'Cancelled', allowedTransitions: [] }), {
          headers: { ETag: `"v${loads}"` },
        })
      }),
      http.put(statusUrl(order), () =>
        problem(412, {
          title: 'This changed since you loaded it',
          detail: 'Refresh to see the latest version, then try again.',
          code: 'precondition_failed',
        }),
      ),
    )
    const { user } = renderRoute(`/orders/${order.id}`)

    await user.click(await screen.findByRole('button', { name: 'Mark as paid' }))

    // The reload shows the order as someone else left it: cancelled, with nothing left to do.
    expect(await screen.findByText("This order is cancelled and can't change any further.")).toBeInTheDocument()
    expect(loads).toBe(2)
  })

  it('shows the error and keeps the actions when a change is rejected', async () => {
    const order = pendingOrder()
    serveOrder(order, () => order)
    server.use(
      http.put(statusUrl(order), () =>
        problem(403, { title: "You don't have access to this", detail: 'Ask an administrator.', correlationId: 'ref-403' }),
      ),
    )
    const { user } = renderRoute(`/orders/${order.id}`)

    await user.click(await screen.findByRole('button', { name: 'Mark as paid' }))

    const alert = (await screen.findByText("You don't have access to this")).closest<HTMLElement>('[role=alert]')!
    expect(alert).toHaveTextContent('ref-403')
    expect(screen.getByRole('button', { name: 'Mark as paid' })).toBeEnabled()
  })

  it("says so when the order doesn't exist", async () => {
    server.use(http.get(url('/api/v1/orders/missing'), () => problem(404, { title: "We couldn't find that", code: 'not_found' })))
    renderRoute('/orders/missing')

    expect(await screen.findByRole('heading', { name: "We couldn't find that order" })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument()
  })
})
