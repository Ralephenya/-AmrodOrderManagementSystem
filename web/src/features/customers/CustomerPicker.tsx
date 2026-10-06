import { Loader2, Search, X } from 'lucide-react'
import { useEffect, useId, useRef, useState } from 'react'
import { useCustomer, useCustomers } from '@/api/queries'
import type { Customer } from '@/api/types'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { cn } from '@/lib/utils'
import { CustomerAvatar } from './CustomerAvatar'

interface Props {
  label: string
  value: string | undefined
  onChange(customer: Customer | undefined): void
  error?: string
  className?: string
}

const RESULT_LIMIT = 8

/**
 * Search-as-you-type customer chooser. The API matches names and emails that start with the text typed.
 * Once a customer is chosen it shows as a card with a clear button.
 */
export function CustomerPicker({ label, value, onChange, error, className }: Props) {
  const id = useId()
  const [text, setText] = useState('')
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)
  const search = useDebouncedValue(text.trim())
  const selected = useCustomer(value)
  // Results appear once the user starts typing, so an idle picker doesn't list every customer.
  const searching = search.length > 0
  const results = useCustomers({ search, page: 1, pageSize: RESULT_LIMIT, sort: 'name' }, { enabled: searching })
  const errorId = `${id}-error`

  // Close the results when the user clicks or taps anywhere outside the picker.
  useEffect(() => {
    if (!open) return
    const onPointerDown = (e: PointerEvent) => {
      if (!rootRef.current?.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('pointerdown', onPointerDown)
    return () => document.removeEventListener('pointerdown', onPointerDown)
  }, [open])

  if (value) {
    return (
      <div className={cn('grid min-w-0 content-start gap-2', className)}>
        <span className="text-sm leading-none font-medium" id={`${id}-label`}>
          {label}
        </span>
        <div
          className="flex h-9 items-center gap-2.5 rounded-md border bg-card pr-1 pl-1.5 shadow-xs"
          aria-labelledby={`${id}-label`}
          role="group"
        >
          {selected.data ? (
            <>
              <CustomerAvatar name={selected.data.name} size="sm" />
              <span className="min-w-0 truncate text-sm">
                {selected.data.name} <span className="text-muted-foreground">({selected.data.email})</span>
              </span>
            </>
          ) : (
            <span className="text-sm text-muted-foreground">Loading customer…</span>
          )}
          <Button
            type="button"
            variant="ghost"
            size="icon-sm"
            className="ml-auto size-7"
            onClick={() => {
              setText('')
              onChange(undefined)
            }}
            aria-label={`Change ${label.toLowerCase()}`}
          >
            <X />
          </Button>
        </div>
      </div>
    )
  }

  const items = results.data?.items ?? []
  return (
    <div ref={rootRef} className={cn('relative grid min-w-0 content-start gap-2', className)}>
      <Label htmlFor={id}>{label}</Label>
      <div className="relative">
        <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
        <Input
          id={id}
          type="search"
          value={text}
          placeholder="Search by name or email"
          autoComplete="off"
          className="pl-9"
          aria-controls={`${id}-results`}
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? errorId : undefined}
          onFocus={() => setOpen(true)}
          onClick={() => setOpen(true)}
          onKeyDown={(e) => {
            if (e.key === 'Escape') setOpen(false)
          }}
          onChange={(e) => {
            setText(e.target.value)
            setOpen(true)
          }}
        />
      </div>
      {error && (
        <p id={errorId} className="text-xs font-medium text-destructive">
          {error}
        </p>
      )}
      <ul
        id={`${id}-results`}
        aria-label={`${label} results`}
        hidden={!searching || !open}
        className="absolute top-full right-0 left-0 z-20 mt-1 max-h-72 overflow-y-auto rounded-md border bg-popover p-1 text-popover-foreground shadow-lg animate-in fade-in-0 zoom-in-95"
      >
        {results.isError && <li className="px-2 py-3 text-sm text-destructive">Couldn't search customers. Try again.</li>}
        {results.isPending && (
          <li className="flex items-center gap-2 px-2 py-3 text-sm text-muted-foreground">
            <Loader2 className="size-4 animate-spin" aria-hidden="true" /> Searching…
          </li>
        )}
        {results.isSuccess && items.length === 0 && (
          <li className="px-2 py-3 text-sm text-muted-foreground">No customers match “{search}”.</li>
        )}
        {items.map((customer) => (
          <li key={customer.id}>
            <button
              type="button"
              className="flex w-full items-center gap-3 rounded-sm px-2 py-2 text-left text-sm outline-none hover:bg-accent focus-visible:bg-accent"
              onClick={() => onChange(customer)}
            >
              <CustomerAvatar name={customer.name} size="sm" />
              <span className="min-w-0 flex-1">
                <span className="block truncate font-medium">{customer.name}</span>
                <span className="block truncate text-xs text-muted-foreground">{customer.email}</span>
              </span>
              <span className="rounded border px-1.5 py-0.5 font-mono text-[11px] text-muted-foreground">{customer.countryCode}</span>
            </button>
          </li>
        ))}
      </ul>
    </div>
  )
}
