import { ChevronRight, PackageOpen, Plus } from 'lucide-react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { useOrders } from '@/api/queries'
import { ORDER_STATUSES } from '@/api/types'
import { EmptyState } from '@/components/EmptyState'
import { PageHeader } from '@/components/PageHeader'
import { Pagination } from '@/components/Pagination'
import { ProblemAlert } from '@/components/ProblemAlert'
import { TableSkeleton } from '@/components/Skeleton'
import { StatusBadge } from '@/components/StatusBadge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Label } from '@/components/ui/label'
import { NativeSelect } from '@/components/ui/native-select'
import { Table, TableBody, TableCaption, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatDateTime } from '@/lib/dates'
import { formatMoney } from '@/lib/money'
import { useSearchParamUpdater } from '@/lib/useSearchParamUpdater'
import { cn } from '@/lib/utils'
import { CustomerAvatar } from '../customers/CustomerAvatar'
import { CustomerPicker } from '../customers/CustomerPicker'

const PAGE_SIZE = 20

const SORTS = [
  { value: '-createdAt', label: 'Newest first' },
  { value: 'createdAt', label: 'Oldest first' },
  { value: '-total', label: 'Total (high to low)' },
  { value: 'total', label: 'Total (low to high)' },
] as const

export function OrdersPage() {
  // Filters, sort and page live in the URL: links from the customers page and refreshes keep them.
  const [params] = useSearchParams()
  const update = useSearchParamUpdater()
  const navigate = useNavigate()
  const page = Math.max(1, Number(params.get('page')) || 1)
  const sort = params.get('sort') ?? '-createdAt'
  const customerId = params.get('customerId') ?? undefined
  const statusParam = params.get('status')
  const status = ORDER_STATUSES.find((s) => s === statusParam)

  const orders = useOrders({ customerId, status, page, pageSize: PAGE_SIZE, sort })
  const filtered = Boolean(customerId || status)

  return (
    <section aria-labelledby="orders-title">
      <PageHeader
        titleId="orders-title"
        title="Orders"
        description="Every order, its status and total, in the currency it was placed in."
        actions={
          <Button asChild>
            <Link to={customerId ? `/orders/new?customerId=${customerId}` : '/orders/new'}>
              <Plus />
              New order
            </Link>
          </Button>
        }
      />

      {/* Status quick filters */}
      <div role="group" aria-label="Filter by status" className="mb-4 flex flex-wrap gap-1.5">
        {[undefined, ...ORDER_STATUSES].map((s) => (
          <Button
            key={s ?? 'all'}
            type="button"
            variant={status === s ? 'default' : 'outline'}
            size="sm"
            aria-pressed={status === s}
            className="rounded-full"
            onClick={() => update({ status: s, page: undefined })}
          >
            {s ?? 'All'}
          </Button>
        ))}
      </div>

      <Card className="gap-0 overflow-visible py-0">
        <div className="flex flex-col gap-3 border-b p-4 md:flex-row md:items-end">
          <CustomerPicker
            className="flex-1"
            label="Customer"
            value={customerId}
            onChange={(c) => update({ customerId: c?.id, page: undefined })}
          />
          <div className="grid gap-2 md:w-52">
            <Label htmlFor="order-sort">Sort by</Label>
            <NativeSelect id="order-sort" value={sort} onChange={(e) => update({ sort: e.target.value, page: undefined })}>
              {SORTS.map((s) => (
                <option key={s.value} value={s.value}>
                  {s.label}
                </option>
              ))}
            </NativeSelect>
          </div>
        </div>

        {orders.isPending ? (
          <TableSkeleton columns={5} label="Loading orders" />
        ) : orders.isError ? (
          <div className="p-4">
            <ProblemAlert error={orders.error} onRetry={() => void orders.refetch()} />
          </div>
        ) : orders.data.items.length === 0 ? (
          <EmptyState
            icon={PackageOpen}
            title={filtered ? 'No orders match these filters.' : 'No orders yet.'}
            description={filtered ? 'Try another status or customer.' : 'Create an order for a customer to get started.'}
          />
        ) : (
          <>
            <Table aria-busy={orders.isFetching} className={cn(orders.isFetching && 'opacity-60 transition-opacity')}>
              <TableCaption className="sr-only">Orders</TableCaption>
              <TableHeader>
                <TableRow className="hover:bg-transparent">
                  <TableHead className="pl-4">Order</TableHead>
                  <TableHead className="hidden sm:table-cell">Customer</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead className="text-right">Total</TableHead>
                  <TableHead className="pr-4">
                    <span className="sr-only">Actions</span>
                  </TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {orders.data.items.map((o) => (
                  <TableRow key={o.id} className="cursor-pointer" onClick={() => void navigate(`/orders/${o.id}`)}>
                    <TableCell className="pl-4">
                      <span className="block font-mono text-xs text-muted-foreground">#{o.id.slice(0, 8)}</span>
                      <span className="block text-sm">{formatDateTime(o.createdAt)}</span>
                      <span className="block text-xs text-muted-foreground sm:hidden">{o.customerName}</span>
                    </TableCell>
                    <TableCell className="hidden sm:table-cell">
                      <div className="flex items-center gap-2.5">
                        <CustomerAvatar name={o.customerName} size="sm" />
                        <span className="font-medium">{o.customerName}</span>
                      </div>
                    </TableCell>
                    <TableCell>
                      <StatusBadge status={o.status} />
                    </TableCell>
                    <TableCell className="text-right font-medium tabular-nums">{formatMoney(o.totalAmount, o.currencyCode)}</TableCell>
                    <TableCell className="pr-4 text-right">
                      <Button asChild variant="ghost" size="icon-sm" onClick={(e) => e.stopPropagation()}>
                        <Link
                          to={`/orders/${o.id}`}
                          aria-label={`View order for ${o.customerName} created ${formatDateTime(o.createdAt)}`}
                        >
                          <ChevronRight />
                        </Link>
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
            <Pagination label="Order" {...orders.data} onPageChange={(p) => update({ page: String(p) })} />
          </>
        )}
      </Card>
    </section>
  )
}
