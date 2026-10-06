import { expect, type Page } from '@playwright/test'

/** A value no other run has used, so journeys don't collide on the unique email. */
export function unique(prefix: string): string {
  return `${prefix}-${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`
}

export interface NewCustomer {
  name: string
  email: string
  country: string
}

export function newCustomer(country = 'Namibia'): NewCustomer {
  const tag = unique('e2e')
  return { name: `E2E Traders ${tag}`, email: `${tag}@example.na`, country }
}

/** Creates a customer through the UI and waits for the confirmation. */
export async function createCustomer(page: Page, customer: NewCustomer) {
  await page.goto('/customers/new')
  await page.getByRole('textbox', { name: 'Name' }).fill(customer.name)
  await page.getByRole('textbox', { name: 'Email' }).fill(customer.email)
  await page.getByRole('combobox', { name: 'Country' }).selectOption({ label: customer.country })
  await page.getByRole('button', { name: 'Create customer' }).click()
  await expect(page.getByText(`Customer ${customer.name} created.`)).toBeVisible()
  await expect(page).toHaveURL(/\/customers$/)
}

export interface Line {
  sku: string
  quantity: number
  unitPrice: string
}

/** From the customers list, starts a new order for `customer` and submits `lines`. Ends on the order page. */
export async function createOrder(page: Page, customer: NewCustomer, lines: Line[], currency?: string) {
  await page.goto('/customers')
  await page.getByRole('searchbox', { name: 'Search' }).fill(customer.name)
  const row = page.getByRole('row').filter({ hasText: customer.email })
  await row.getByRole('link', { name: 'New order' }).click()

  await expect(page.getByRole('heading', { name: 'New order' })).toBeVisible()
  await expect(page.getByRole('group', { name: 'Customer' })).toContainText(customer.name)
  if (currency) {
    await page.getByRole('combobox', { name: 'Currency' }).selectOption(currency)
  }

  for (const [i, line] of lines.entries()) {
    if (i > 0) await page.getByRole('button', { name: 'Add line' }).click()
    await page.getByRole('textbox', { name: `Line ${i + 1} SKU` }).fill(line.sku)
    await page.getByRole('textbox', { name: `Line ${i + 1} quantity` }).fill(String(line.quantity))
    await page.getByRole('textbox', { name: `Line ${i + 1} unit price` }).fill(line.unitPrice)
  }
  await page.getByRole('button', { name: 'Create order' }).click()

  await expect(page.getByText('Order created.')).toBeVisible()
  await expect(page).toHaveURL(/\/orders\/[0-9a-f-]{36}$/)
}

/** Intl output uses non-breaking spaces; compare money text with ordinary spaces. */
export function money(text: string): RegExp {
  return new RegExp(text.replace(/ /g, '\\s'))
}
