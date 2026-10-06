const formatters = new Map<string, Intl.NumberFormat>()

/**
 * Formats an amount in its own currency with the currency's minor units (KMF has none, ZAR has two).
 * `minorUnits` comes from the reference data; without it, Intl's own default for the currency is used.
 */
export function formatMoney(amount: number, currencyCode: string, minorUnits?: number, locale = 'en-ZA'): string {
  const key = `${locale}|${currencyCode}|${minorUnits ?? ''}`
  let formatter = formatters.get(key)
  if (!formatter) {
    formatter = new Intl.NumberFormat(locale, {
      style: 'currency',
      currency: currencyCode,
      currencyDisplay: 'code',
      ...(minorUnits === undefined ? {} : { minimumFractionDigits: minorUnits, maximumFractionDigits: minorUnits }),
    })
    formatters.set(key, formatter)
  }
  return formatter.format(amount)
}

/** Number of decimals in a typed amount, for "no more decimals than the currency allows". */
export function decimalPlaces(value: string): number {
  const fraction = value.trim().split('.')[1]
  return fraction ? fraction.length : 0
}
