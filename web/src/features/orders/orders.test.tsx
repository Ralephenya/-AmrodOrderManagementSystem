import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { etosha, page, pendingOrder, summaryOf, thandi } from '../../test/fixtures'
import { renderRoute } from '../../test/render'
import { server, url } from '../../test/server'

describe('Orders page', () => {
  it('lists orders with status and total in their currency', async () => {
    server.use(
      http.get(url('/api/v1/orders'), () =>
        HttpResponse.json(page([summaryOf(pendingOrder()), summaryOf(pendingOrder({ id: 'o2', status: 'Paid', currencyCode: 'NAD', totalAmount: 80 }), etosha.name)])),
      ),
    )
    renderRoute('/orders')

    const table = await screen.findByRole('table', { name: 'Orders' })
    const [, first, second] = within(table).getAllByRole('row')
    expect(first).toHaveTextContent('Thandi Mokoena')
    expect(first).toHaveTextContent('Pending')
    expect(first!.textContent!.replace(/\s/g, ' ')).toContain('ZAR 1 249,50')
    expect(second).toHaveTextContent('Paid')
    expect(second!.textContent!.replace(/\s/g, ' ')).toContain('NAD 80,00')
  })

  it('filters by customer and status, and sorts, through the query string', async () => {
    const queries: string[] = []
    server.use(
      http.get(url('/api/v1/orders'), ({ request }) => {
        queries.push(new URL(request.url).search)
        return HttpResponse.json(page([]))
      }),
    )
    const { user } = renderRoute(`/orders?customerId=${thandi.id}`)

    await waitFor(() => expect(screen.getByRole('group', { name: 'Customer' })).toHaveTextContent(`${thandi.name} (${thandi.email})`))
    await user.click(screen.getByRole('button', { name: 'Paid' }))
    expect(screen.getByRole('button', { name: 'Paid' })).toHaveAttribute('aria-pressed', 'true')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Sort by' }), 'Total (high to low)')

    await waitFor(() => {
      const last = new URLSearchParams(queries.at(-1))
      expect(last.get('customerId')).toBe(thandi.id)
      expect(last.get('status')).toBe('Paid')
      expect(last.get('sort')).toBe('-total')
    })
    expect(screen.getByText('No orders match these filters.')).toBeInTheDocument()
    expect(within(screen.getByRole('main')).getByRole('link', { name: 'New order' })).toHaveAttribute(
      'href',
      `/orders/new?customerId=${thandi.id}`,
    )
  })
})
