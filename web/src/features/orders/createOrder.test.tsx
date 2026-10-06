import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { etosha, moroni, pendingOrder, thandi } from '../../test/fixtures'
import { renderRoute } from '../../test/render'
import { server, url } from '../../test/server'

// Intl output uses non-breaking spaces.
const plain = (s: string | null) => (s ?? '').replace(/\s/g, ' ')

describe('New order form', () => {
  it("offers only the currencies the customer's country accepts, local currency first", async () => {
    renderRoute(`/orders/new?customerId=${etosha.id}`)

    await waitFor(() => expect(screen.getByRole('group', { name: 'Customer' })).toHaveTextContent(`${etosha.name} (${etosha.email})`))
    const currency = screen.getByRole('combobox', { name: 'Currency' })
    await waitFor(() => expect(currency).toHaveValue('NAD'))
    expect(within(currency).getAllByRole('option').map((o) => o.textContent)).toEqual([
      'NAD · Namibian dollar',
      'ZAR · South African rand',
    ])
    expect(screen.getByText('Currencies accepted in Namibia.')).toBeInTheDocument()
  })

  it('lets you pick the customer by searching', async () => {
    const { user } = renderRoute('/orders/new')

    expect(await screen.findByRole('combobox', { name: 'Currency' })).toBeDisabled()
    expect(screen.queryByRole('button', { name: /Thandi Mokoena/ })).not.toBeInTheDocument() // nothing listed until you type
    await user.type(screen.getByRole('searchbox', { name: 'Customer' }), 'Thandi')
    await user.click(await screen.findByRole('button', { name: /Thandi Mokoena/ }))

    await waitFor(() => expect(screen.getByRole('group', { name: 'Customer' })).toHaveTextContent(`${thandi.name} (${thandi.email})`))
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Currency' })).toHaveValue('ZAR'))
  })

  it('closes the customer results on Escape and on a click outside', async () => {
    const { user } = renderRoute('/orders/new')
    const search = await screen.findByRole('searchbox', { name: 'Customer' })

    await user.type(search, 'Thandi')
    expect(await screen.findByRole('button', { name: /Thandi Mokoena/ })).toBeInTheDocument()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('button', { name: /Thandi Mokoena/ })).not.toBeInTheDocument()

    await user.click(search) // clicking the field again reopens them
    expect(await screen.findByRole('button', { name: /Thandi Mokoena/ })).toBeInTheDocument()
    await user.click(screen.getByRole('heading', { name: 'New order' }))
    expect(screen.queryByRole('button', { name: /Thandi Mokoena/ })).not.toBeInTheDocument()
  })

  it('creates the order and opens it', async () => {
    let body: unknown
    const created = pendingOrder({ id: 'created-order-id', currencyCode: 'ZAR' })
    server.use(
      http.post(url('/api/v1/orders'), async ({ request }) => {
        body = await request.json()
        return HttpResponse.json(created, { status: 201, headers: { ETag: '"v1"' } })
      }),
      http.get(url('/api/v1/orders/created-order-id'), () => HttpResponse.json(created, { headers: { ETag: '"v1"' } })),
    )
    const { user, router } = renderRoute(`/orders/new?customerId=${thandi.id}`)
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Currency' })).toHaveValue('ZAR'))

    await user.type(screen.getByRole('textbox', { name: 'Line 1 SKU' }), 'mug-01')
    await user.clear(screen.getByRole('textbox', { name: 'Line 1 quantity' }))
    await user.type(screen.getByRole('textbox', { name: 'Line 1 quantity' }), '10')
    await user.type(screen.getByRole('textbox', { name: 'Line 1 unit price' }), '99.95')
    await user.click(screen.getByRole('button', { name: 'Add line' }))
    await user.type(screen.getByRole('textbox', { name: 'Line 2 SKU' }), 'PEN-02')
    await user.type(screen.getByRole('textbox', { name: 'Line 2 unit price' }), '10')

    expect(plain(screen.getByText('Estimated total').closest('p')!.textContent)).toContain('ZAR 1 009,50')
    await user.click(screen.getByRole('button', { name: 'Create order' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/orders/created-order-id'))
    expect(body).toEqual({
      customerId: thandi.id,
      currencyCode: 'ZAR',
      lineItems: [
        { productSku: 'mug-01', quantity: 10, unitPrice: 99.95 },
        { productSku: 'PEN-02', quantity: 1, unitPrice: 10 },
      ],
    })
    expect(await screen.findByText('Order created.')).toBeInTheDocument()
  })

  it("rejects decimals a currency doesn't have, and duplicate SKUs, before sending", async () => {
    let posted = false
    server.use(
      http.post(url('/api/v1/orders'), () => {
        posted = true
        return HttpResponse.json({}, { status: 201 })
      }),
    )
    const { user } = renderRoute(`/orders/new?customerId=${moroni.id}`)
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Currency' })).toHaveValue('KMF'))

    await user.type(screen.getByRole('textbox', { name: 'Line 1 SKU' }), 'rice-5kg')
    await user.type(screen.getByRole('textbox', { name: 'Line 1 unit price' }), '1500.50')
    await user.click(screen.getByRole('button', { name: 'Add line' }))
    await user.type(screen.getByRole('textbox', { name: 'Line 2 SKU' }), 'RICE-5KG')
    await user.type(screen.getByRole('textbox', { name: 'Line 2 unit price' }), '1500')
    await user.click(screen.getByRole('button', { name: 'Create order' }))

    expect(await screen.findByRole('textbox', { name: 'Line 1 unit price' })).toHaveAccessibleDescription(
      "KMF prices can't have decimals.",
    )
    expect(screen.getByText(/SKU RICE-5KG appears on more than one line/)).toBeInTheDocument()
    expect(posted).toBe(false)
  })

  it("shows the API's business-rule errors on the matching fields", async () => {
    server.use(
      http.post(url('/api/v1/orders'), () =>
        HttpResponse.json(
          {
            title: 'Some details need fixing',
            status: 400,
            code: 'validation_failed',
            errors: {
              currencyCode: ['Customers in South Africa can only order in ZAR, not USD.'],
              'lineItems[0].quantity': ['Line 1: only 3 in stock.'],
            },
          },
          { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )
    const { user } = renderRoute(`/orders/new?customerId=${thandi.id}`)
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Currency' })).toHaveValue('ZAR'))

    await user.type(screen.getByRole('textbox', { name: 'Line 1 SKU' }), 'MUG-01')
    await user.type(screen.getByRole('textbox', { name: 'Line 1 unit price' }), '5')
    await user.click(screen.getByRole('button', { name: 'Create order' }))

    expect(await screen.findByText('Customers in South Africa can only order in ZAR, not USD.')).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Line 1 quantity' })).toHaveAccessibleDescription('Line 1: only 3 in stock.')
  })
})
