import AxeBuilder from '@axe-core/playwright'
import { expect, test } from '@playwright/test'
import { createCustomer, createOrder, newCustomer } from './helpers'

/** axe-core WCAG 2.1 A/AA scan. Serious and critical violations fail the test; the rest are reported. */
async function scan(page: import('@playwright/test').Page) {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()
  const blocking = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical')
  const summary = blocking.map((v) => `${v.id} (${v.impact}): ${v.help} → ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`)
  expect(summary, 'serious or critical accessibility violations').toEqual([])
}

for (const theme of ['light', 'dark'] as const) {
  test.describe(`Accessibility (${theme})`, () => {
    test.beforeEach(async ({ page }) => {
      await page.addInitScript((t) => localStorage.setItem('om-theme', t), theme)
    })

    for (const [name, path, heading] of [
      ['dashboard', '/dashboard', 'Dashboard'],
      ['orders', '/orders', 'Orders'],
      ['customers', '/customers', 'Customers'],
      ['new customer', '/customers/new', 'New customer'],
      ['new order', '/orders/new', 'New order'],
    ] as const) {
      test(`${name} page`, async ({ page }) => {
        await page.goto(path)
        await expect(page.getByRole('heading', { level: 1, name: heading })).toBeVisible()
        await page.waitForLoadState('networkidle')
        await scan(page)
      })
    }

    test('order details page', async ({ page }) => {
      const customer = newCustomer('South Africa')
      await createCustomer(page, customer)
      await createOrder(page, customer, [{ sku: 'a11y-01', quantity: 1, unitPrice: '10' }])
      await page.waitForLoadState('networkidle')
      await scan(page)
    })
  })
}
