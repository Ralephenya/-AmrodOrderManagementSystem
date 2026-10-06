import { ArrowLeft, Ban, Check, CircleCheckBig, CreditCard, Loader2, PackageCheck, Warehouse } from 'lucide-react'
import { useState, type ComponentType } from 'react'
import { Link, useParams } from 'react-router'
import { ApiError } from '@/api/problem'
import { findCountry, isAwaitingAllocation, useChangeOrderStatus, useCountries, useCustomer, useOrder } from '@/api/queries'
import type { Order, OrderStatus } from '@/api/types'
import { PageHeader } from '@/components/PageHeader'
import { ProblemAlert } from '@/components/ProblemAlert'
import { TableSkeleton } from '@/components/Skeleton'
import { StatusBadge } from '@/components/StatusBadge'
import { useToast } from '@/components/useToast'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from '@/components/ui/alert-dialog'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Separator } from '@/components/ui/separator'
import { Table, TableBody, TableCell, TableFooter, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatDateTime } from '@/lib/dates'
import { newIdempotencyKey } from '@/lib/idempotency'
import { formatMoney } from '@/lib/money'
import { cn } from '@/lib/utils'
import { CustomerAvatar } from '../customers/CustomerAvatar'

const ACTION_LABELS: Record<OrderStatus, string> = {
  Pending: 'Mark pending',
  Paid: 'Mark as paid',
  Fulfilled: 'Mark as fulfilled',
  Cancelled: 'Cancel order',
}

const ACTION_ICONS: Partial<Record<OrderStatus, ComponentType>> = {
  Paid: CreditCard,
  Fulfilled: PackageCheck,
}

export function OrderDetailsPage() {
  const { id = '' } = useParams()
  const order = useOrder(id)

  if (order.isPending) {
    return (
      <div className="rounded-xl border bg-card">
        <TableSkeleton columns={4} rows={5} label="Loading order" />
      </div>
    )
  }
  if (order.isError) {
    const notFound = order.error instanceof ApiError && order.error.status === 404
    return (
      <section aria-labelledby="order-title">
        <BackLink />
        <PageHeader titleId="order-title" title={notFound ? "We couldn't find that order" : 'Order'} />
        <ProblemAlert error={order.error} onRetry={notFound ? undefined : () => void order.refetch()} />
      </section>
    )
  }
  return <OrderDetails order={order.data.order} etag={order.data.etag} />
}

function BackLink() {
  return (
    <Button asChild variant="ghost" size="sm" className="mb-4 -ml-2 text-muted-foreground">
      <Link to="/orders">
        <ArrowLeft />
        Orders
      </Link>
    </Button>
  )
}

function OrderDetails({ order, etag }: { order: Order; etag: string | null }) {
  const customer = useCustomer(order.customerId)
  const countries = useCountries()
  const minorUnits = findCountry(countries.data, customer.data?.countryCode)?.currencies.find(
    (c) => c.code === order.currencyCode,
  )?.minorUnits
  const money = (amount: number) => formatMoney(amount, order.currencyCode, minorUnits)

  // useOrder polls while this is true, so the page updates when the worker allocates stock.
  const awaitingAllocation = isAwaitingAllocation(order)
  const units = order.lineItems.reduce((sum, l) => sum + l.quantity, 0)

  return (
    <section aria-labelledby="order-title">
      <BackLink />
      <PageHeader
        titleId="order-title"
        title={
          <span className="flex flex-wrap items-center gap-3">
            <span>
              Order <span className="font-mono text-muted-foreground">#{order.id.slice(0, 8)}</span>
            </span>
            <StatusBadge status={order.status} />
          </span>
        }
        description={`Placed ${formatDateTime(order.createdAt)} · ${order.currencyCode}`}
      />

      <Progress order={order} />

      <div className="mt-6 grid items-start gap-6 xl:grid-cols-[minmax(0,1fr)_340px]">
        <Card className="order-2 gap-0 pb-0 xl:order-1">
          <CardHeader className="pb-4">
            <CardTitle>Line items</CardTitle>
            <CardDescription>
              {order.lineItems.length} {order.lineItems.length === 1 ? 'line' : 'lines'} · {units} units
            </CardDescription>
          </CardHeader>
          <Table>
            <TableHeader>
              <TableRow className="hover:bg-transparent">
                <TableHead className="pl-6">SKU</TableHead>
                <TableHead className="text-right">Quantity</TableHead>
                <TableHead className="text-right">Unit price</TableHead>
                <TableHead className="pr-6 text-right">Line total</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {order.lineItems.map((line) => (
                <TableRow key={line.id}>
                  <TableCell className="pl-6 font-mono text-[13px] font-medium">{line.productSku}</TableCell>
                  <TableCell className="text-right tabular-nums">{line.quantity}</TableCell>
                  <TableCell className="text-right text-muted-foreground tabular-nums">{money(line.unitPrice)}</TableCell>
                  <TableCell className="pr-6 text-right font-medium tabular-nums">{money(line.lineTotal)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
            <TableFooter>
              <TableRow className="hover:bg-transparent">
                <TableHead scope="row" colSpan={3} className="pl-6 text-foreground">
                  Total
                </TableHead>
                <TableCell className="pr-6 text-right text-base font-semibold tabular-nums">{money(order.totalAmount)}</TableCell>
              </TableRow>
            </TableFooter>
          </Table>
        </Card>

        <div className="order-1 grid gap-6 md:grid-cols-2 xl:order-2 xl:grid-cols-1">
          <StatusActions order={order} etag={etag} awaitingAllocation={awaitingAllocation} total={money(order.totalAmount)} />

          <Card>
            <CardHeader>
              <CardTitle>Summary</CardTitle>
            </CardHeader>
            <CardContent>
              <dl className="grid gap-4 text-sm">
                <div>
                  <dt className="mb-1.5 text-xs text-muted-foreground">Customer</dt>
                  <dd>
                    {customer.data ? (
                      <Link
                        to={`/orders?customerId=${order.customerId}`}
                        className="flex items-center gap-2.5 font-medium hover:underline"
                      >
                        <CustomerAvatar name={customer.data.name} />
                        {customer.data.name}
                      </Link>
                    ) : (
                      '…'
                    )}
                  </dd>
                </div>
                <Separator />
                <SummaryItem label="Created" value={formatDateTime(order.createdAt)} />
                <SummaryItem
                  label="Stock"
                  value={order.allocatedAt ? `Allocated ${formatDateTime(order.allocatedAt)}` : 'Not allocated yet'}
                />
                <SummaryItem label="Currency" value={order.currencyCode} />
                <Separator />
                <div className="flex items-baseline justify-between">
                  <dt className="text-muted-foreground">Total</dt>
                  <dd className="text-xl font-semibold tracking-tight tabular-nums">{money(order.totalAmount)}</dd>
                </div>
              </dl>
            </CardContent>
          </Card>
        </div>
      </div>
    </section>
  )
}

function SummaryItem({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-4">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="text-right font-medium">{value}</dd>
    </div>
  )
}

/**
 * Where the order is in its life. The worker allocates stock as soon as an order is placed, so allocation comes
 * before payment; a paid order is fulfilled once both are done.
 */
function Progress({ order }: { order: Order }) {
  if (order.status === 'Cancelled') {
    return (
      <div className="flex items-center gap-3 rounded-xl border border-status-cancelled/30 bg-status-cancelled/8 px-4 py-3 text-sm">
        <Ban className="size-4 shrink-0 text-status-cancelled" aria-hidden="true" />
        <span>This order was cancelled and won't be fulfilled.</span>
      </div>
    )
  }

  const paid = order.status === 'Paid' || order.status === 'Fulfilled'
  const steps = [
    { label: 'Placed', done: true, icon: Check },
    { label: 'Stock allocated', done: Boolean(order.allocatedAt), icon: Warehouse },
    { label: 'Paid', done: paid, icon: CreditCard },
    { label: 'Fulfilled', done: order.status === 'Fulfilled', icon: CircleCheckBig },
  ]
  const current = steps.findIndex((s) => !s.done)

  return (
    <Card className="py-5">
      <CardContent>
        <ol aria-label="Order progress" className="grid grid-cols-4 gap-2">
          {steps.map((step, i) => {
            const Icon = step.icon
            const active = i === current
            const allocating = active && step.label === 'Stock allocated'
            return (
              <li key={step.label} className="relative flex flex-col items-center gap-2 text-center">
                {i > 0 && (
                  <span
                    aria-hidden="true"
                    className={cn('absolute top-4 right-1/2 h-0.5 w-full -translate-y-1/2', step.done ? 'bg-primary' : 'bg-border')}
                  />
                )}
                <span
                  className={cn(
                    'relative z-10 flex size-8 items-center justify-center rounded-full border-2 bg-card transition-colors',
                    step.done && 'border-primary bg-primary text-primary-foreground',
                    active && 'border-primary text-primary',
                    !step.done && !active && 'text-muted-foreground',
                  )}
                >
                  {allocating ? (
                    <Loader2 className="size-4 animate-spin" aria-hidden="true" />
                  ) : (
                    <Icon className="size-4" aria-hidden="true" />
                  )}
                </span>
                <span className={cn('text-xs font-medium', !step.done && !active && 'text-muted-foreground')}>
                  {step.label}
                  <span className="sr-only">{step.done ? ' (done)' : active ? ' (current step)' : ' (to do)'}</span>
                </span>
              </li>
            )
          })}
        </ol>
      </CardContent>
    </Card>
  )
}

/**
 * Only the transitions the API says are legal right now. Each click sends a new Idempotency-Key (a retry of the
 * same click reuses it) and the ETag the user saw as If-Match, so a change made elsewhere meanwhile is caught.
 */
function StatusActions({
  order,
  etag,
  awaitingAllocation,
  total,
}: {
  order: Order
  etag: string | null
  awaitingAllocation: boolean
  total: string
}) {
  const change = useChangeOrderStatus(order.id)
  const toast = useToast()
  const [error, setError] = useState<unknown>(null)

  // mutateAsync, not mutate(vars, { onSuccess }): per-call callbacks only run while the mutation still has a
  // subscriber, and in the browser the toast for a final transition (Fulfilled) went missing with them.
  // The promise settles regardless.
  const run = async (status: OrderStatus) => {
    setError(null)
    try {
      const { order: updated } = await change.mutateAsync({ status, idempotencyKey: newIdempotencyKey(), etag })
      toast.success(`Order is now ${updated.status.toLowerCase()}.`)
    } catch (e) {
      setError(e)
    }
  }

  const terminal = order.allowedTransitions.length === 0
  const forward = order.allowedTransitions.filter((t) => t !== 'Cancelled')
  const canCancel = order.allowedTransitions.includes('Cancelled')

  return (
    <Card>
      <CardHeader>
        <CardTitle id="actions-title">Actions</CardTitle>
        {terminal && <CardDescription>This order is {order.status.toLowerCase()} and can't change any further.</CardDescription>}
      </CardHeader>
      {!terminal && (
        <CardContent className="grid gap-3" role="group" aria-labelledby="actions-title">
          {error !== null && <ProblemAlert error={error} />}
          {forward.map((target) => {
            const blocked = target === 'Fulfilled' && awaitingAllocation
            const Icon = ACTION_ICONS[target]
            const saving = change.isPending && change.variables?.status === target
            return (
              <Button
                key={target}
                type="button"
                className="w-full"
                disabled={change.isPending || blocked}
                aria-describedby={blocked ? 'allocation-hint' : undefined}
                onClick={() => void run(target)}
              >
                {saving ? <Loader2 className="animate-spin" /> : Icon && <Icon />}
                {saving ? 'Saving…' : ACTION_LABELS[target]}
              </Button>
            )
          })}
          {awaitingAllocation && order.allowedTransitions.includes('Fulfilled') && (
            <p id="allocation-hint" role="status" className="flex items-center gap-2 rounded-md bg-muted px-3 py-2 text-xs text-muted-foreground">
              <Loader2 className="size-3.5 shrink-0 animate-spin" aria-hidden="true" />
              Waiting for stock to be allocated before this order can be fulfilled…
            </p>
          )}
          {canCancel && (
            <AlertDialog>
              <AlertDialogTrigger asChild>
                <Button
                  type="button"
                  variant="outline"
                  className="w-full text-destructive hover:bg-destructive/10 hover:text-destructive"
                  disabled={change.isPending}
                >
                  <Ban />
                  {ACTION_LABELS.Cancelled}
                </Button>
              </AlertDialogTrigger>
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>Cancel this order?</AlertDialogTitle>
                  <AlertDialogDescription>
                    The {total} order will be cancelled and can't be reopened.
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>Keep it</AlertDialogCancel>
                  <AlertDialogAction variant="destructive" onClick={() => void run('Cancelled')}>
                    Yes, cancel it
                  </AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          )}
        </CardContent>
      )}
    </Card>
  )
}
