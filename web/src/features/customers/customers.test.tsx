import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { customers, page } from '../../test/fixtures'
import { renderRoute } from '../../test/render'
import { problem, server, url } from '../../test/server'

describe('Customers page', () => {
  it('lists customers with their country names and links to their orders', async () => {
    renderRoute('/customers')

    const table = await screen.findByRole('table', { name: 'Customers' })
    const rows = within(table).getAllByRole('row')
    expect(rows).toHaveLength(customers.length + 1) // plus the header row
    expect(within(table).getByRole('cell', { name: 'Namibia' })).toBeInTheDocument()
    expect(within(rows[1]!).getByRole('link', { name: 'Orders' })).toHaveAttribute(
      'href',
      `/orders?customerId=${customers[0]!.id}`,
    )
  })

  it('searches as you type and keeps the search in the URL', async () => {
    const searches: (string | null)[] = []
    server.use(
      http.get(url('/api/v1/customers'), ({ request }) => {
        searches.push(new URL(request.url).searchParams.get('search'))
        return HttpResponse.json(page([]))
      }),
    )
    const { user, router } = renderRoute('/customers')

    await user.type(await screen.findByRole('searchbox', { name: 'Search' }), 'Eto')

    expect(await screen.findByText('No customers match “Eto”.')).toBeInTheDocument()
    expect(searches.at(-1)).toBe('Eto')
    expect(router.state.location.search).toBe('?search=Eto')
  })

  it('pages through results', async () => {
    const pages: (string | null)[] = []
    server.use(
      http.get(url('/api/v1/customers'), ({ request }) => {
        const p = new URL(request.url).searchParams.get('page')
        pages.push(p)
        return HttpResponse.json(page(customers, Number(p), 3, 7))
      }),
    )
    const { user } = renderRoute('/customers')

    expect(await screen.findByText(/Page 1 of 3 · 7 results/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled()
    await user.click(screen.getByRole('button', { name: 'Next' }))

    expect(await screen.findByText(/Page 2 of 3 · 7 results/)).toBeInTheDocument()
    expect(pages).toEqual(['1', '2'])
  })

  it('shows a friendly error with the support reference when the list fails', async () => {
    server.use(
      http.get(url('/api/v1/customers'), () =>
        problem(500, { title: 'Something went wrong on our side', detail: 'Please try again.', correlationId: 'ref-500' }),
      ),
    )
    renderRoute('/customers')

    const alert = (await screen.findByText('Something went wrong on our side')).closest<HTMLElement>('[role=alert]')!
    expect(alert).toHaveTextContent('Please try again.')
    expect(alert).toHaveTextContent('ref-500')
    expect(within(alert).getByRole('button', { name: 'Try again' })).toBeInTheDocument()
  })
})

describe('New customer form', () => {
  it('validates before sending anything', async () => {
    let posted = false
    server.use(
      http.post(url('/api/v1/customers'), () => {
        posted = true
        return HttpResponse.json({}, { status: 201 })
      }),
    )
    const { user } = renderRoute('/customers/new')

    await user.type(await screen.findByRole('textbox', { name: 'Email' }), 'not-an-email')
    await user.click(screen.getByRole('button', { name: 'Create customer' }))

    expect(await screen.findByText("Please enter the customer's name.")).toBeInTheDocument()
    expect(screen.getByText('Please enter a valid email address, like name@example.co.za.')).toBeInTheDocument()
    expect(screen.getByText("Please choose the customer's country.")).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Name' })).toHaveAttribute('aria-invalid', 'true')
    expect(posted).toBe(false)
  })

  it('creates the customer, confirms it and returns to the list', async () => {
    let body: unknown
    server.use(
      http.post(url('/api/v1/customers'), async ({ request }) => {
        body = await request.json()
        return HttpResponse.json(
          { id: 'new-id', name: 'Lerato Dlamini', email: 'lerato@example.co.ls', countryCode: 'NA', createdAt: '2026-10-06T10:00:00Z' },
          { status: 201 },
        )
      }),
    )
    const { user, router } = renderRoute('/customers/new')

    await user.type(await screen.findByRole('textbox', { name: 'Name' }), 'Lerato Dlamini')
    await user.type(screen.getByRole('textbox', { name: 'Email' }), 'lerato@example.co.ls')
    await user.selectOptions(await screen.findByRole('combobox', { name: 'Country' }), 'Namibia')
    await user.click(screen.getByRole('button', { name: 'Create customer' }))

    expect(await screen.findByText('Customer Lerato Dlamini created.')).toBeInTheDocument()
    expect(body).toEqual({ name: 'Lerato Dlamini', email: 'lerato@example.co.ls', countryCode: 'NA' })
    await waitFor(() => expect(router.state.location.pathname).toBe('/customers'))
  })

  it('shows a duplicate email on the email field', async () => {
    server.use(
      http.post(url('/api/v1/customers'), () =>
        problem(409, {
          title: 'That change conflicts with the current state',
          detail: 'A customer with the email address taken@example.com already exists.',
          code: 'email_already_exists',
        }),
      ),
    )
    const { user } = renderRoute('/customers/new')

    await user.type(await screen.findByRole('textbox', { name: 'Name' }), 'Someone')
    await user.type(screen.getByRole('textbox', { name: 'Email' }), 'taken@example.com')
    await user.selectOptions(await screen.findByRole('combobox', { name: 'Country' }), 'South Africa')
    await user.click(screen.getByRole('button', { name: 'Create customer' }))

    const email = screen.getByRole('textbox', { name: 'Email' })
    await waitFor(() => expect(email).toHaveAccessibleDescription(/already exists/))
    expect(email).toHaveAttribute('aria-invalid', 'true')
  })
})
