import type { Country, Customer, Order, OrderSummary } from '../api/types'

export const countries: Country[] = [
  {
    code: 'ZA',
    name: 'South Africa',
    isCommonMonetaryArea: true,
    currencies: [{ code: 'ZAR', name: 'South African rand', minorUnits: 2 }],
  },
  {
    code: 'NA',
    name: 'Namibia',
    isCommonMonetaryArea: true,
    currencies: [
      { code: 'NAD', name: 'Namibian dollar', minorUnits: 2 },
      { code: 'ZAR', name: 'South African rand', minorUnits: 2 },
    ],
  },
  {
    code: 'KM',
    name: 'Comoros',
    isCommonMonetaryArea: false,
    currencies: [{ code: 'KMF', name: 'Comorian franc', minorUnits: 0 }],
  },
]

export const thandi: Customer = {
  id: '11111111-1111-4111-8111-111111111111',
  name: 'Thandi Mokoena',
  email: 'thandi@example.co.za',
  countryCode: 'ZA',
  createdAt: '2026-10-01T08:00:00Z',
}

export const etosha: Customer = {
  id: '22222222-2222-4222-8222-222222222222',
  name: 'Etosha Traders',
  email: 'orders@etosha.example.na',
  countryCode: 'NA',
  createdAt: '2026-10-02T09:30:00Z',
}

export const moroni: Customer = {
  id: '33333333-3333-4333-8333-333333333333',
  name: 'Moroni Supplies',
  email: 'hello@moroni.example.km',
  countryCode: 'KM',
  createdAt: '2026-10-03T10:00:00Z',
}

export const customers = [thandi, etosha, moroni]

export function pendingOrder(overrides: Partial<Order> = {}): Order {
  return {
    id: '44444444-4444-4444-8444-444444444444',
    customerId: thandi.id,
    status: 'Pending',
    currencyCode: 'ZAR',
    totalAmount: 1249.5,
    createdAt: '2026-10-05T12:00:00Z',
    allocatedAt: null,
    allowedTransitions: ['Paid', 'Cancelled'],
    lineItems: [
      { id: 'l1', productSku: 'MUG-01', quantity: 10, unitPrice: 99.95, lineTotal: 999.5 },
      { id: 'l2', productSku: 'PEN-02', quantity: 25, unitPrice: 10, lineTotal: 250 },
    ],
    ...overrides,
  }
}

export function summaryOf(order: Order, customerName = thandi.name): OrderSummary {
  return {
    id: order.id,
    customerId: order.customerId,
    customerName,
    status: order.status,
    currencyCode: order.currencyCode,
    totalAmount: order.totalAmount,
    createdAt: order.createdAt,
  }
}

export function page<T>(items: T[], pageNumber = 1, pageSize = 20, totalCount = items.length) {
  const totalPages = totalCount === 0 ? 0 : Math.ceil(totalCount / pageSize)
  return {
    items,
    page: pageNumber,
    pageSize,
    totalCount,
    totalPages,
    hasPrevious: pageNumber > 1,
    hasNext: pageNumber < totalPages,
  }
}
