import { zodResolver } from '@hookform/resolvers/zod'
import { ArrowLeft, Loader2, Plus, Trash2 } from 'lucide-react'
import { useEffect, useMemo } from 'react'
import { useFieldArray, useForm, useWatch } from 'react-hook-form'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { z } from 'zod'
import { ApiError } from '@/api/problem'
import { findCountry, useCountries, useCreateOrder, useCustomer } from '@/api/queries'
import type { Currency } from '@/api/types'
import { Field } from '@/components/Field'
import { PageHeader } from '@/components/PageHeader'
import { ProblemAlert } from '@/components/ProblemAlert'
import { useToast } from '@/components/useToast'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { NativeSelect } from '@/components/ui/native-select'
import { Separator } from '@/components/ui/separator'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { applyServerErrors } from '@/lib/forms'
import { decimalPlaces, formatMoney } from '@/lib/money'
import { CustomerPicker } from '../customers/CustomerPicker'

const MAX_LINES = 100
// The API's quantity is a 32-bit int; anything larger fails JSON binding before validation runs.
const MAX_QUANTITY = 2_147_483_647

/**
 * Built per currency, because how many decimals a price may have depends on it (ZAR 2, KMF 0).
 * Mirrors the API's rules; the API stays the authority and its errors land on the same fields.
 */
function buildSchema(currency: Currency | undefined) {
  const line = z.object({
    productSku: z.string().trim().min(1, 'Please enter a product SKU.').max(64, 'Product SKU can be at most 64 characters.'),
    quantity: z
      .string()
      .trim()
      .regex(/^\d+$/, 'Quantity must be a whole number.')
      .refine((q) => Number(q) >= 1, 'Quantity must be at least 1.')
      .refine((q) => Number(q) <= MAX_QUANTITY, `Quantity can be at most ${MAX_QUANTITY.toLocaleString('en-ZA')}.`),
    unitPrice: z
      .string()
      .trim()
      .regex(/^\d+(\.\d+)?$/, 'Enter a price like 199.99.')
      .refine(
        (p) => !currency || decimalPlaces(p) <= currency.minorUnits,
        currency
          ? currency.minorUnits === 0
            ? `${currency.code} prices can't have decimals.`
            : `${currency.code} prices can have at most ${currency.minorUnits} decimal places.`
          : 'Invalid price.',
      ),
  })

  return z.object({
    customerId: z.string().min(1, 'Please choose the customer this order is for.'),
    currencyCode: z.string().min(1, 'Please choose a currency.'),
    lineItems: z
      .array(line)
      .min(1, 'An order needs at least one line item.')
      .max(MAX_LINES, `An order can have at most ${MAX_LINES} line items.`)
      .superRefine((lines, ctx) => {
        const seen = new Set<string>()
        for (const l of lines) {
          const sku = l.productSku.trim().toUpperCase()
          if (sku && seen.has(sku)) {
            ctx.addIssue({
              code: 'custom',
              message: `SKU ${sku} appears on more than one line. Combine them into a single line with the total quantity.`,
            })
            return
          }
          seen.add(sku)
        }
      }),
  })
}

type FormValues = z.infer<ReturnType<typeof buildSchema>>

const emptyLine = { productSku: '', quantity: '1', unitPrice: '' }

export function CreateOrderPage() {
  const [params] = useSearchParams()
  const countries = useCountries()
  const create = useCreateOrder()
  const navigate = useNavigate()
  const toast = useToast()

  const form = useForm<FormValues>({
    // Resolve against the currently chosen currency on every validation.
    resolver: (values, context, options) => {
      const country = findCountry(countries.data, customer.data?.countryCode)
      const currency = country?.currencies.find((c) => c.code === values.currencyCode)
      return zodResolver(buildSchema(currency))(values, context, options)
    },
    defaultValues: { customerId: params.get('customerId') ?? '', currencyCode: '', lineItems: [emptyLine] },
  })
  const lines = useFieldArray({ control: form.control, name: 'lineItems' })
  const [customerId, currencyCode, watchedLines] = useWatch({
    control: form.control,
    name: ['customerId', 'currencyCode', 'lineItems'],
  })

  const customer = useCustomer(customerId || undefined)
  const country = findCountry(countries.data, customer.data?.countryCode)
  const currencies = useMemo(() => country?.currencies ?? [], [country])
  const currency = currencies.find((c) => c.code === currencyCode)

  // A customer's currencies are listed local currency first, so that's the sensible default.
  useEffect(() => {
    if (currencies.length > 0 && !currencies.some((c) => c.code === form.getValues('currencyCode'))) {
      form.setValue('currencyCode', currencies[0]!.code, { shouldValidate: form.formState.isSubmitted })
    }
  }, [currencies, form])

  // Display only: the API computes the real total.
  const estimatedTotal = watchedLines.reduce((sum, l) => sum + lineTotal(l.quantity, l.unitPrice), 0)

  const { errors, isSubmitting } = form.formState
  const linesError = errors.lineItems?.message ?? errors.lineItems?.root?.message

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      const { order } = await create.mutateAsync({
        customerId: values.customerId,
        currencyCode: values.currencyCode,
        lineItems: values.lineItems.map((l) => ({
          productSku: l.productSku.trim(),
          quantity: Number(l.quantity),
          unitPrice: Number(l.unitPrice),
        })),
      })
      toast.success('Order created.')
      await navigate(`/orders/${order.id}`)
    } catch (error) {
      // An error about the lines as a whole (duplicate SKUs) goes above the list, not on one line.
      const shown = applyServerErrors(error, form.setError, ['customerId', 'currencyCode', 'lineItems'], {
        lineItems: 'lineItems.root',
      })
      if (!shown) {
        toast.error(error instanceof ApiError ? error.title : "Couldn't create the order.")
      }
    }
  })

  const lineCount = watchedLines.filter((l) => l.productSku.trim()).length
  const units = watchedLines.reduce((sum, l) => sum + (Number(l.quantity) || 0), 0)
  const money = (amount: number) => (currency ? formatMoney(amount, currency.code, currency.minorUnits) : '—')

  return (
    <section aria-labelledby="new-order-title">
      <Button asChild variant="ghost" size="sm" className="mb-4 -ml-2 text-muted-foreground">
        <Link to="/orders">
          <ArrowLeft />
          Orders
        </Link>
      </Button>
      <PageHeader
        titleId="new-order-title"
        title="New order"
        description="Prices are in the order's currency. The server calculates the final total."
      />

      {countries.isError && <ProblemAlert error={countries.error} onRetry={() => void countries.refetch()} />}

      <form onSubmit={(e) => void onSubmit(e)} noValidate className="grid items-start gap-6 xl:grid-cols-[minmax(0,1fr)_320px]">
        <div className="grid min-w-0 gap-6">
          <Card className="overflow-visible">
            <CardHeader>
              <CardTitle>Customer & currency</CardTitle>
              <CardDescription>The customer's country decides which currencies they can order in.</CardDescription>
            </CardHeader>
            <CardContent className="grid gap-5 sm:grid-cols-2">
              <CustomerPicker
                label="Customer"
                value={customerId || undefined}
                error={errors.customerId?.message}
                onChange={(c) => form.setValue('customerId', c?.id ?? '', { shouldValidate: form.formState.isSubmitted })}
              />
              <Field
                label="Currency"
                error={errors.currencyCode?.message}
                hint={country ? `Currencies accepted in ${country.name}.` : 'Choose a customer first.'}
              >
                <NativeSelect disabled={currencies.length === 0} {...form.register('currencyCode')}>
                  {currencies.length === 0 && <option value="">—</option>}
                  {currencies.map((c) => (
                    <option key={c.code} value={c.code}>
                      {c.code} · {c.name}
                    </option>
                  ))}
                </NativeSelect>
              </Field>
            </CardContent>
          </Card>

          <Card className="gap-0 pb-0">
            <fieldset className="contents" aria-labelledby="line-items-title">
              <CardHeader className="pb-4">
                <h2 id="line-items-title" className="leading-none font-semibold">Line items</h2>
                <CardDescription>Up to {MAX_LINES} lines. Each SKU may appear once.</CardDescription>
              </CardHeader>
              {linesError && (
                <div className="px-6 pb-4">
                  <p role="alert" className="rounded-md bg-destructive/10 px-3 py-2 text-sm font-medium text-destructive">
                    {linesError}
                  </p>
                </div>
              )}
              <Table>
                <TableHeader>
                  <TableRow className="hover:bg-transparent">
                    <TableHead className="pl-6">SKU</TableHead>
                    <TableHead className="w-28">Quantity</TableHead>
                    <TableHead className="w-40">Unit price{currency ? ` (${currency.code})` : ''}</TableHead>
                    <TableHead className="w-36 text-right">Line total</TableHead>
                    <TableHead className="w-12 pr-6">
                      <span className="sr-only">Remove</span>
                    </TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {lines.fields.map((field, index) => {
                    const lineErrors = errors.lineItems?.[index]
                    const line = watchedLines[index]
                    return (
                      <TableRow key={field.id} className="align-top hover:bg-transparent">
                        <TableCell className="pl-6">
                          <LineInput
                            label={`Line ${index + 1} SKU`}
                            placeholder="MUG-01"
                            error={lineErrors?.productSku?.message}
                            {...form.register(`lineItems.${index}.productSku`)}
                          />
                        </TableCell>
                        <TableCell>
                          <LineInput
                            label={`Line ${index + 1} quantity`}
                            inputMode="numeric"
                            error={lineErrors?.quantity?.message}
                            {...form.register(`lineItems.${index}.quantity`)}
                          />
                        </TableCell>
                        <TableCell>
                          <LineInput
                            label={`Line ${index + 1} unit price`}
                            inputMode="decimal"
                            placeholder="0.00"
                            error={lineErrors?.unitPrice?.message}
                            {...form.register(`lineItems.${index}.unitPrice`)}
                          />
                        </TableCell>
                        <TableCell className="pt-4 text-right font-medium tabular-nums">
                          {line ? money(lineTotal(line.quantity, line.unitPrice)) : '—'}
                        </TableCell>
                        <TableCell className="pr-6">
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon-sm"
                            className="text-muted-foreground hover:text-destructive"
                            disabled={lines.fields.length === 1}
                            onClick={() => lines.remove(index)}
                            aria-label={`Remove line ${index + 1}`}
                          >
                            <Trash2 />
                          </Button>
                        </TableCell>
                      </TableRow>
                    )
                  })}
                </TableBody>
              </Table>
              <div className="border-t px-6 py-3">
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  disabled={lines.fields.length >= MAX_LINES}
                  onClick={() => lines.append(emptyLine)}
                >
                  <Plus />
                  Add line
                </Button>
              </div>
            </fieldset>
          </Card>
        </div>

        <Card className="xl:sticky xl:top-24">
          <CardHeader>
            <CardTitle>Summary</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-3 text-sm">
            <SummaryRow label="Customer" value={customer.data?.name ?? '—'} />
            <SummaryRow label="Currency" value={currency?.code ?? '—'} />
            <SummaryRow label="Lines" value={String(lineCount)} />
            <SummaryRow label="Units" value={String(units)} />
            <Separator className="my-1" />
            <p className="flex items-baseline justify-between gap-4" aria-live="polite">
              <span className="text-muted-foreground">Estimated total</span>
              <span className="text-xl font-semibold tracking-tight tabular-nums">{money(estimatedTotal)}</span>
            </p>
          </CardContent>
          <CardFooter className="flex-col gap-2 border-t">
            <Button type="submit" className="w-full" disabled={isSubmitting}>
              {isSubmitting && <Loader2 className="animate-spin" />}
              {isSubmitting ? 'Creating…' : 'Create order'}
            </Button>
            <Button asChild variant="ghost" className="w-full">
              <Link to="/orders">Cancel</Link>
            </Button>
          </CardFooter>
        </Card>
      </form>
    </section>
  )
}

function SummaryRow({ label, value }: { label: string; value: string }) {
  return (
    <p className="flex justify-between gap-4">
      <span className="text-muted-foreground">{label}</span>
      <span className="truncate font-medium">{value}</span>
    </p>
  )
}

function lineTotal(quantity: string, unitPrice: string): number {
  const q = Number(quantity)
  const p = Number(unitPrice)
  return Number.isFinite(q) && Number.isFinite(p) ? q * p : 0
}

type LineInputProps = React.ComponentProps<'input'> & { label: string; error?: string }

/** A table-cell input: the label is visually hidden (the column header shows it) but still announced. */
function LineInput({ label, error, id, ...input }: LineInputProps) {
  const inputId = id ?? `${input.name}-input`
  const errorId = `${inputId}-error`
  return (
    <div className="grid gap-1">
      <label htmlFor={inputId} className="sr-only">
        {label}
      </label>
      <Input
        id={inputId}
        type="text"
        autoComplete="off"
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? errorId : undefined}
        {...input}
      />
      {error && (
        <p id={errorId} className="text-xs font-medium text-destructive">
          {error}
        </p>
      )}
    </div>
  )
}
