import { describe, expect, it } from 'vitest'
import { decimalPlaces, formatMoney } from './money'

// Intl output uses non-breaking spaces; normalise them so expectations read naturally.
const plain = (s: string) => s.replace(/\s/g, ' ')

describe('formatMoney', () => {
  it('shows the currency code and its minor units', () => {
    expect(plain(formatMoney(1249.5, 'ZAR', 2))).toBe('ZAR 1 249,50')
  })

  it('shows no decimals for a currency without minor units', () => {
    expect(plain(formatMoney(1500, 'KMF', 0))).toBe('KMF 1 500')
  })

  it("falls back to Intl's own minor units when the reference data isn't loaded", () => {
    expect(plain(formatMoney(10, 'USD'))).toBe('USD 10,00')
  })
})

describe('decimalPlaces', () => {
  it.each([
    ['12', 0],
    ['12.5', 1],
    ['12.50', 2],
    [' 0.125 ', 3],
  ])('%s has %i decimal places', (value, expected) => {
    expect(decimalPlaces(value)).toBe(expected)
  })
})
