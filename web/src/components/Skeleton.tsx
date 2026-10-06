import { Skeleton } from '@/components/ui/skeleton'

/** Placeholder rows while a table loads. Hidden from assistive tech, which hears the status message instead. */
export function TableSkeleton({ columns, rows = 6, label }: { columns: number; rows?: number; label: string }) {
  return (
    <>
      <p className="sr-only" role="status">
        {label}
      </p>
      <div aria-hidden="true" className="divide-y">
        {Array.from({ length: rows }, (_, r) => (
          <div key={r} className="grid gap-6 px-4 py-3.5" style={{ gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))` }}>
            {Array.from({ length: columns }, (_, c) => (
              <Skeleton key={c} className="h-4" style={{ width: `${55 + ((r * 7 + c * 13) % 40)}%` }} />
            ))}
          </div>
        ))}
      </div>
    </>
  )
}
