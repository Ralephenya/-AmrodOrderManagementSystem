import type { OrderStatus } from '@/api/types'
import { cn } from '@/lib/utils'

const STYLES: Record<OrderStatus, string> = {
  Pending: 'bg-status-pending/12 ring-status-pending/25',
  Paid: 'bg-status-paid/12 ring-status-paid/25',
  Fulfilled: 'bg-status-fulfilled/12 ring-status-fulfilled/25',
  Cancelled: 'bg-status-cancelled/12 ring-status-cancelled/25',
}

const DOTS: Record<OrderStatus, string> = {
  Pending: 'bg-status-pending',
  Paid: 'bg-status-paid',
  Fulfilled: 'bg-status-fulfilled',
  Cancelled: 'bg-status-cancelled',
}

export function StatusBadge({ status, className }: { status: OrderStatus; className?: string }) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-medium text-foreground ring-1 ring-inset',
        STYLES[status],
        className,
      )}
    >
      <span aria-hidden="true" className={cn('size-1.5 rounded-full', DOTS[status])} />
      {status}
    </span>
  )
}
