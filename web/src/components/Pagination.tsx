import { ChevronLeft, ChevronRight } from 'lucide-react'
import { Button } from '@/components/ui/button'

interface Props {
  page: number
  totalPages: number
  totalCount: number
  pageSize: number
  hasPrevious: boolean
  hasNext: boolean
  onPageChange(page: number): void
  label: string
}

export function Pagination({ page, totalPages, totalCount, pageSize, hasPrevious, hasNext, onPageChange, label }: Props) {
  if (totalCount === 0) {
    return null
  }
  const from = (page - 1) * pageSize + 1
  const to = Math.min(page * pageSize, totalCount)
  return (
    <nav
      aria-label={`${label} pages`}
      className="flex items-center justify-between gap-4 border-t px-4 py-3 text-sm text-muted-foreground"
    >
      <span>
        Showing <span className="font-medium text-foreground">{from}</span>–<span className="font-medium text-foreground">{to}</span> of{' '}
        <span className="font-medium text-foreground">{totalCount}</span>
        <span className="sr-only">
          {' '}
          · Page {page} of {totalPages} · {totalCount} {totalCount === 1 ? 'result' : 'results'}
        </span>
      </span>
      <div className="flex items-center gap-2">
        <Button variant="outline" size="sm" disabled={!hasPrevious} onClick={() => onPageChange(page - 1)}>
          <ChevronLeft />
          Previous
        </Button>
        <Button variant="outline" size="sm" disabled={!hasNext} onClick={() => onPageChange(page + 1)}>
          Next
          <ChevronRight />
        </Button>
      </div>
    </nav>
  )
}
