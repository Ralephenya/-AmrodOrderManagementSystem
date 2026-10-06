import { expect, test } from '@playwright/test'
import { createCustomer, createOrder, money, newCustomer } from './helpers'

test.describe('Order journey', () => {
  test('create a customer, order for them, pay, and the worker fulfils it', async ({ page }) => {
    const customer = newCustomer('Namibia')
    await createCustomer(page, customer)

    await createOrder(page, customer, [
      { sku: 'mug-blue', quantity: 3, unitPrice: '49.99' },
      { sku: 'pen-02', quantity: 10, unitPrice: '12.50' },
    ])

    // The server priced it: 3 × 49.99 + 10 × 12.50 = 274.97, in Namibia's local currency.
    await expect(page.getByRole('main')).toContainText(money('NAD 274,97'))
    await expect(page.getByRole('cell', { name: 'MUG-BLUE' })).toBeVisible() // SKUs are stored upper case
    await expect(page.getByText('Pending', { exact: true }).first()).toBeVisible()

    await page.getByRole('button', { name: 'Mark as paid' }).click()
    await expect(page.getByText('Order is now paid.')).toBeVisible()

    // The worker allocates stock (on creation) and fulfils the paid order. The page polls and shows it.
    await expect(page.getByText("This order is fulfilled and can't change any further.")).toBeVisible({ timeout: 30_000 })
    await expect(page.getByRole('list', { name: 'Order progress' })).not.toContainText('to do')

    // The list reflects it too.
    await page.getByRole('link', { name: customer.name }).click()
    const row = page.getByRole('row').filter({ hasText: customer.name })
    await expect(row).toContainText('Fulfilled')
    await expect(row).toContainText(money('NAD 274,97'))
  })

  test('an order can be cancelled, after confirming', async ({ page }) => {
    const customer = newCustomer('South Africa')
    await createCustomer(page, customer)
    await createOrder(page, customer, [{ sku: 'cap-01', quantity: 2, unitPrice: '80' }])

    await page.getByRole('button', { name: 'Cancel order' }).click()
    const dialog = page.getByRole('alertdialog', { name: 'Cancel this order?' })
    await expect(dialog).toContainText(money('ZAR 160,00'))
    await dialog.getByRole('button', { name: 'Keep it' }).click()
    await expect(dialog).toBeHidden()
    await expect(page.getByRole('button', { name: 'Mark as paid' })).toBeEnabled()

    await page.getByRole('button', { name: 'Cancel order' }).click()
    await page.getByRole('alertdialog').getByRole('button', { name: 'Yes, cancel it' }).click()

    await expect(page.getByText('Order is now cancelled.')).toBeVisible()
    await expect(page.getByText("This order is cancelled and can't change any further.")).toBeVisible()
    await expect(page.getByRole('button', { name: 'Mark as paid' })).toHaveCount(0)
  })

  test('a CMA customer can order in their own currency or in rand', async ({ page }) => {
    const customer = newCustomer('Lesotho')
    await createCustomer(page, customer)

    await page.goto('/customers')
    await page.getByRole('searchbox', { name: 'Search' }).fill(customer.name)
    await page.getByRole('row').filter({ hasText: customer.email }).getByRole('link', { name: 'New order' }).click()

    const currency = page.getByRole('combobox', { name: 'Currency' })
    await expect(currency).toHaveValue('LSL')
    await expect(currency.locator('option')).toHaveText(['LSL · Lesotho loti', 'ZAR · South African rand'])

    await createOrder(page, customer, [{ sku: 'bag-07', quantity: 1, unitPrice: '350' }], 'ZAR')
    await expect(page.getByRole('heading', { level: 1 })).toContainText('Order')
    await expect(page.getByText(/Placed .* · ZAR/)).toBeVisible()
  })
})

test.describe('Validation', () => {
  test('a duplicate email is reported on the email field', async ({ page }) => {
    const customer = newCustomer('Botswana')
    await createCustomer(page, customer)

    await page.goto('/customers/new')
    await page.getByRole('textbox', { name: 'Name' }).fill('Someone Else')
    await page.getByRole('textbox', { name: 'Email' }).fill(customer.email.toUpperCase()) // unique case-insensitively
    await page.getByRole('combobox', { name: 'Country' }).selectOption({ label: 'Botswana' })
    await page.getByRole('button', { name: 'Create customer' }).click()

    const email = page.getByRole('textbox', { name: 'Email' })
    await expect(email).toHaveAttribute('aria-invalid', 'true')
    await expect(email).toHaveAccessibleDescription(/already exists/)
    await expect(page).toHaveURL(/\/customers\/new$/)
  })

  test('prices are checked against the currency before anything is sent', async ({ page }) => {
    const customer = newCustomer('Namibia')
    await createCustomer(page, customer)

    await page.goto('/customers')
    await page.getByRole('searchbox', { name: 'Search' }).fill(customer.name)
    await page.getByRole('row').filter({ hasText: customer.email }).getByRole('link', { name: 'New order' }).click()

    let posted = false
    page.on('request', (r) => {
      if (r.method() === 'POST' && r.url().endsWith('/api/v1/orders')) posted = true
    })
    await page.getByRole('textbox', { name: 'Line 1 SKU' }).fill('mug-01')
    await page.getByRole('textbox', { name: 'Line 1 unit price' }).fill('9.999')
    await page.getByRole('button', { name: 'Create order' }).click()

    await expect(page.getByRole('textbox', { name: 'Line 1 unit price' })).toHaveAccessibleDescription(
      'NAD prices can have at most 2 decimal places.',
    )
    expect(posted).toBe(false)
  })
})
