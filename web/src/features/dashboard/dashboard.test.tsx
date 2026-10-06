import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { etosha, page, pendingOrder, summaryOf, thandi } from '@/test/fixtures'
import { renderRoute } from '@/test/render'
import { server, url } from '@/test/server'

const COUNTS: Record<string, number> = { Pending: 3, Paid: 2, Fulfilled: 7, Cancelled: 1 }

function serveDashboard(onReport?: (query: URLSearchParams) => void) {
  server.use(
    http.get(url('/api/v1/orders'), ({ request }) => {
      const status = new URL(request.url).searchParams.get('status')
      if (status) return HttpResponse.json(page([], 1, 1, COUNTS[status]))
      return HttpResponse.json(page([summaryOf(pendingOrder())]))
    }),
    http.get(url('/api/v1/reports/top-spenders'), ({ request }) => {
      const query = new URL(request.url).searchParams
      onReport?.(query)
      return HttpResponse.json({
        currencyCode: query.get('currency'),
        sinceUtc: '2026-07-08T00:00:00Z',
        untilUtc: '2026-10-06T00:00:00Z',
        customers: [
          { rank: 1, customerId: etosha.id, name: etosha.name, countryCode: 'NA', totalSpend: 5400, orderCount: 3 },
          { rank: 2, customerId: thandi.id, name: thandi.name, countryCode: 'ZA', totalSpend: 1250.5, orderCount: 1 },
          { rank: 3, customerId: 'zero', name: 'No Spend Yet', countryCode: 'ZA', totalSpend: 0, orderCount: 0 },
        ],
      })
    }),
  )
}

describe('Dashboard', () => {
  it('is the landing page', async () => {
    serveDashboard()
    const { router } = renderRoute('/')

    await waitFor(() => expect(router.state.location.pathname).toBe('/dashboard'))
    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
  })

  it('shows how many orders are in each status, linking to that filtered list', async () => {
    serveDashboard()
    renderRoute('/dashboard')

    for (const [status, count] of Object.entries(COUNTS)) {
      const link = await screen.findByRole('link', { name: `${count} ${status.toLowerCase()} orders` })
      expect(link).toHaveAttribute('href', `/orders?status=${status}`)
    }
  })

  it('ranks top spenders in one currency, leaving out customers with no spend', async () => {
    const reports: URLSearchParams[] = []
    serveDashboard((q) => reports.push(q))
    const { user } = renderRoute('/dashboard')

    const list = await screen.findByRole('list', { name: 'Top spenders in ZAR' })
    const items = within(list).getAllByRole('listitem')
    expect(items).toHaveLength(2)
    expect(items[0]).toHaveTextContent(etosha.name)
    expect(items[0]!.textContent!.replace(/\s/g, ' ')).toContain('ZAR 5 400,00')
    expect(reports[0]!.get('days')).toBe('90')

    await user.selectOptions(screen.getByRole('combobox', { name: 'Currency' }), 'NAD')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Period' }), 'Last 30 days')

    expect(await screen.findByRole('list', { name: 'Top spenders in NAD' })).toBeInTheDocument()
    await waitFor(() => {
      expect(reports.at(-1)!.get('currency')).toBe('NAD')
      expect(reports.at(-1)!.get('days')).toBe('30')
    })
  })
})

describe('Theme', () => {
  it("doesn't save the OS preference as if the user had chosen it", async () => {
    localStorage.removeItem('om-theme')
    serveDashboard()
    renderRoute('/dashboard')

    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
    expect(localStorage.getItem('om-theme')).toBeNull()
  })

  it('toggles dark mode on the document and remembers it', async () => {
    serveDashboard()
    const { user } = renderRoute('/dashboard')
    const startDark = document.documentElement.classList.contains('dark')

    await user.click(await screen.findByRole('button', { name: startDark ? 'Switch to light mode' : 'Switch to dark mode' }))

    expect(document.documentElement.classList.contains('dark')).toBe(!startDark)
    expect(localStorage.getItem('om-theme')).toBe(startDark ? 'light' : 'dark')
  })
})
