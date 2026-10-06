import { ArrowRight, CircleCheckBig, Clock, CreditCard, Package, Plus, Trophy, XCircle } from 'lucide-react'
import { useMemo, useState, type ComponentType } from 'react'
import { Link } from 'react-router'
import { Bar, BarChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { useCountries, useOrders, useTopSpenders } from '@/api/queries'
import type { OrderStatus } from '@/api/types'
import { PageHeader } from '@/components/PageHeader'
import { ProblemAlert } from '@/components/ProblemAlert'
import { StatusBadge } from '@/components/StatusBadge'
import { Button } from '@/components/ui/button'
import { Card, CardAction, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Label } from '@/components/ui/label'
import { NativeSelect } from '@/components/ui/native-select'
import { Skeleton } from '@/components/ui/skeleton'
import { formatDateTime } from '@/lib/dates'
import { formatMoney } from '@/lib/money'
import { cn } from '@/lib/utils'
import { CustomerAvatar } from '../customers/CustomerAvatar'

export function DashboardPage() {
  return (
    <section aria-labelledby="dashboard-title">
      <PageHeader
        titleId="dashboard-title"
        title="Dashboard"
        description="Orders across the SADC region at a glance."
        actions={
          <Button asChild>
            <Link to="/orders/new">
              <Plus />
              New order
            </Link>
          </Button>
        }
      />

      <div className="grid grid-cols-2 gap-3 sm:gap-4 xl:grid-cols-4">
        <StatTile status="Pending" label="Pending" hint="Awaiting payment" icon={Clock} />
        <StatTile status="Paid" label="Paid" hint="Ready to fulfil once allocated" icon={CreditCard} />
        <StatTile status="Fulfilled" label="Fulfilled" hint="Shipped to the customer" icon={CircleCheckBig} />
        <StatTile status="Cancelled" label="Cancelled" hint="Won't be fulfilled" icon={XCircle} />
      </div>

      <div className="mt-6 grid items-start gap-6 xl:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <TopSpendersCard />
        <RecentOrdersCard />
      </div>
    </section>
  )
}

const STATUS_TINT: Record<OrderStatus, string> = {
  Pending: 'bg-status-pending/12 text-status-pending',
  Paid: 'bg-status-paid/12 text-status-paid',
  Fulfilled: 'bg-status-fulfilled/12 text-status-fulfilled',
  Cancelled: 'bg-status-cancelled/12 text-status-cancelled',
}

/** One headline number per status: the order list's totalCount for that status (a one-row page). */
function StatTile({
  status,
  label,
  hint,
  icon: Icon,
}: {
  status: OrderStatus
  label: string
  hint: string
  icon: ComponentType<{ className?: string }>
}) {
  const orders = useOrders({ status, page: 1, pageSize: 1 })
  return (
    <Card className="gap-3 py-5">
      <CardHeader className="flex flex-row items-center justify-between gap-2 px-4 sm:px-5">
        <CardDescription className="font-medium text-foreground">{label}</CardDescription>
        <span className={cn('flex size-8 items-center justify-center rounded-lg', STATUS_TINT[status])}>
          <Icon className="size-4" aria-hidden="true" />
        </span>
      </CardHeader>
      <CardContent className="px-4 sm:px-5">
        {orders.isPending ? (
          <Skeleton className="h-8 w-16" />
        ) : orders.isError ? (
          <p className="text-sm text-muted-foreground">Unavailable</p>
        ) : (
          <Link
            to={`/orders?status=${status}`}
            className="text-3xl font-semibold tracking-tight tabular-nums hover:underline"
            aria-label={`${orders.data.totalCount} ${label.toLowerCase()} orders`}
          >
            {orders.data.totalCount}
          </Link>
        )}
        <p className="mt-1 text-xs text-muted-foreground">{hint}</p>
      </CardContent>
    </Card>
  )
}

const WINDOWS = [
  { days: 30, label: 'Last 30 days' },
  { days: 90, label: 'Last 90 days' },
  { days: 366, label: 'Last 12 months' },
] as const

/** Brief Q13 as a picture: customers ranked by Paid + Fulfilled spend, in one currency at a time. */
function TopSpendersCard() {
  const countries = useCountries()
  const currencies = useMemo(() => {
    const byCode = new Map<string, { code: string; name: string; minorUnits: number }>()
    for (const country of countries.data ?? []) for (const c of country.currencies) byCode.set(c.code, c)
    return [...byCode.values()].sort((a, b) => a.code.localeCompare(b.code))
  }, [countries.data])
  const [currency, setCurrency] = useState('ZAR')
  const [days, setDays] = useState(90)
  const report = useTopSpenders(currency, days, 5)
  const minorUnits = currencies.find((c) => c.code === currency)?.minorUnits
  const money = (n: number) => formatMoney(n, currency, minorUnits)

  const rows = (report.data?.customers ?? []).filter((c) => c.totalSpend > 0)

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Trophy className="size-4 text-muted-foreground" aria-hidden="true" />
          Top spenders
        </CardTitle>
        <CardDescription>Paid and fulfilled orders. Spend is never added up across currencies.</CardDescription>
      </CardHeader>
      <CardContent className="grid gap-4">
        <div className="flex flex-wrap gap-2">
          <div>
            <Label htmlFor="spenders-currency" className="sr-only">
              Currency
            </Label>
            <NativeSelect id="spenders-currency" value={currency} onChange={(e) => setCurrency(e.target.value)} className="h-8 w-24">
              {(currencies.length ? currencies : [{ code: 'ZAR' }]).map((c) => (
                <option key={c.code} value={c.code}>
                  {c.code}
                </option>
              ))}
            </NativeSelect>
          </div>
          <div>
            <Label htmlFor="spenders-window" className="sr-only">
              Period
            </Label>
            <NativeSelect id="spenders-window" value={days} onChange={(e) => setDays(Number(e.target.value))} className="h-8 w-36">
              {WINDOWS.map((w) => (
                <option key={w.days} value={w.days}>
                  {w.label}
                </option>
              ))}
            </NativeSelect>
          </div>
        </div>
        {report.isPending ? (
          <Skeleton className="h-56 w-full" />
        ) : report.isError ? (
          <ProblemAlert error={report.error} onRetry={() => void report.refetch()} />
        ) : rows.length === 0 ? (
          <p className="flex h-56 items-center justify-center rounded-lg border border-dashed text-sm text-muted-foreground">
            No paid {currency} orders in this period.
          </p>
        ) : (
          <div className="grid gap-6 md:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
            <div className="h-56" aria-hidden="true">
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={rows} layout="vertical" margin={{ top: 0, right: 8, bottom: 0, left: 0 }} barCategoryGap={10}>
                  <XAxis type="number" hide />
                  <YAxis
                    type="category"
                    // Keyed by ID: two customers can share a name, and a category axis would merge them.
                    dataKey="customerId"
                    width={96}
                    tickLine={false}
                    axisLine={false}
                    tick={{ fill: 'var(--muted-foreground)', fontSize: 12 }}
                    tickFormatter={(id: string) => {
                      const name = rows.find((r) => r.customerId === id)?.name ?? ''
                      return name.length > 13 ? `${name.slice(0, 12)}…` : name
                    }}
                  />
                  <Tooltip
                    cursor={{ fill: 'var(--muted)', opacity: 0.6 }}
                    content={({ active, payload }) => {
                      const row = active ? payload?.[0]?.payload : undefined
                      if (!row) return null
                      return (
                        <div className="rounded-lg border bg-popover px-3 py-2 text-xs shadow-md">
                          <p className="font-medium text-popover-foreground">{row.name}</p>
                          <p className="text-muted-foreground">
                            {money(row.totalSpend)} · {row.orderCount} {row.orderCount === 1 ? 'order' : 'orders'}
                          </p>
                        </div>
                      )
                    }}
                  />
                  <Bar dataKey="totalSpend" fill="var(--chart-1)" radius={[0, 4, 4, 0]} maxBarSize={22} />
                </BarChart>
              </ResponsiveContainer>
            </div>
            {/* The same ranking as a list: readable without the chart and by screen readers. */}
            <ol aria-label={`Top spenders in ${currency}`} className="grid content-start gap-1">
              {rows.map((row) => (
                <li key={row.customerId} className="flex items-center gap-3 rounded-md px-2 py-1.5 hover:bg-muted/60">
                  <span className="w-4 text-xs font-medium text-muted-foreground tabular-nums">{row.rank}</span>
                  <CustomerAvatar name={row.name} size="sm" />
                  <Link to={`/orders?customerId=${row.customerId}`} className="min-w-0 flex-1 truncate text-sm font-medium hover:underline">
                    {row.name}
                  </Link>
                  <span className="text-sm font-medium tabular-nums">{money(row.totalSpend)}</span>
                </li>
              ))}
            </ol>
          </div>
        )}
      </CardContent>
    </Card>
  )
}

function RecentOrdersCard() {
  const recent = useOrders({ page: 1, pageSize: 6, sort: '-createdAt' })
  return (
    <Card className="gap-0 pb-2">
      <CardHeader className="pb-3">
        <CardTitle className="flex items-center gap-2">
          <Package className="size-4 text-muted-foreground" aria-hidden="true" />
          Recent orders
        </CardTitle>
        <CardAction>
          <Button asChild variant="ghost" size="sm">
            <Link to="/orders">
              View all
              <ArrowRight />
            </Link>
          </Button>
        </CardAction>
      </CardHeader>
      <CardContent className="px-2">
        {recent.isPending ? (
          <div className="grid gap-2 px-4 py-2">
            {Array.from({ length: 5 }, (_, i) => (
              <Skeleton key={i} className="h-10" />
            ))}
          </div>
        ) : recent.isError ? (
          <div className="px-4">
            <ProblemAlert error={recent.error} onRetry={() => void recent.refetch()} />
          </div>
        ) : recent.data.items.length === 0 ? (
          <p className="px-4 py-10 text-center text-sm text-muted-foreground">No orders yet.</p>
        ) : (
          <ul className="grid">
            {recent.data.items.map((o) => (
              <li key={o.id}>
                <Link to={`/orders/${o.id}`} className="flex items-center gap-3 rounded-md px-4 py-2.5 hover:bg-muted/60">
                  <CustomerAvatar name={o.customerName} />
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-sm font-medium">{o.customerName}</span>
                    <span className="block text-xs text-muted-foreground">{formatDateTime(o.createdAt)}</span>
                  </span>
                  <span className="flex flex-col items-end gap-1">
                    <span className="text-sm font-medium tabular-nums">{formatMoney(o.totalAmount, o.currencyCode)}</span>
                    <StatusBadge status={o.status} />
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  )
}
