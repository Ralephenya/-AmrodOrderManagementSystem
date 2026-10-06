import { Package, Plus, Search, UserRound } from 'lucide-react'
import { useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { useCountries, useCustomers } from '@/api/queries'
import { EmptyState } from '@/components/EmptyState'
import { PageHeader } from '@/components/PageHeader'
import { Pagination } from '@/components/Pagination'
import { ProblemAlert } from '@/components/ProblemAlert'
import { TableSkeleton } from '@/components/Skeleton'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { NativeSelect } from '@/components/ui/native-select'
import { Table, TableBody, TableCaption, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatDateTime } from '@/lib/dates'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { useSearchParamUpdater } from '@/lib/useSearchParamUpdater'
import { CustomerAvatar } from './CustomerAvatar'

const PAGE_SIZE = 20

const SORTS = [
  { value: 'name', label: 'Name (A–Z)' },
  { value: '-name', label: 'Name (Z–A)' },
  { value: '-createdAt', label: 'Newest first' },
  { value: 'createdAt', label: 'Oldest first' },
] as const

export function CustomersPage() {
  // The page and sort live in the URL so a refresh or a shared link shows the same list.
  const [params] = useSearchParams()
  const update = useSearchParamUpdater()
  const page = Math.max(1, Number(params.get('page')) || 1)
  const sort = params.get('sort') ?? 'name'
  const [text, setText] = useState(params.get('search') ?? '')
  const search = useDebouncedValue(text.trim())

  const customers = useCustomers({ search, page, pageSize: PAGE_SIZE, sort })
  const countries = useCountries()
  const countryName = (code: string) => countries.data?.find((c) => c.code === code)?.name ?? code

  return (
    <section aria-labelledby="customers-title">
      <PageHeader
        titleId="customers-title"
        title="Customers"
        description="Businesses across the SADC region who order from Amrod."
        actions={
          <Button asChild>
            <Link to="/customers/new">
              <Plus />
              New customer
            </Link>
          </Button>
        }
      />

      <Card className="gap-0 overflow-hidden py-0">
        <div role="search" className="flex flex-col gap-3 border-b p-4 sm:flex-row sm:items-end">
          <div className="grid flex-1 gap-2">
            <Label htmlFor="customer-search">Search</Label>
            <div className="relative">
              <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
              <Input
                id="customer-search"
                type="search"
                placeholder="Name or email starts with…"
                className="pl-9"
                value={text}
                onChange={(e) => {
                  setText(e.target.value)
                  update({ search: e.target.value.trim() || undefined, page: undefined }, { replace: true })
                }}
              />
            </div>
          </div>
          <div className="grid gap-2 sm:w-52">
            <Label htmlFor="customer-sort">Sort by</Label>
            <NativeSelect id="customer-sort" value={sort} onChange={(e) => update({ sort: e.target.value, page: undefined })}>
              {SORTS.map((s) => (
                <option key={s.value} value={s.value}>
                  {s.label}
                </option>
              ))}
            </NativeSelect>
          </div>
        </div>

        {customers.isPending ? (
          <TableSkeleton columns={4} label="Loading customers" />
        ) : customers.isError ? (
          <div className="p-4">
            <ProblemAlert error={customers.error} onRetry={() => void customers.refetch()} />
          </div>
        ) : customers.data.items.length === 0 ? (
          <NoCustomers search={search} />
        ) : (
          <>
            <Table aria-busy={customers.isFetching} className={customers.isFetching ? 'opacity-60 transition-opacity' : undefined}>
              <TableCaption className="sr-only">Customers</TableCaption>
              <TableHeader>
                <TableRow className="hover:bg-transparent">
                  <TableHead className="pl-4">Name</TableHead>
                  <TableHead className="hidden md:table-cell">Email</TableHead>
                  <TableHead className="hidden sm:table-cell">Country</TableHead>
                  <TableHead className="hidden lg:table-cell">Created</TableHead>
                  <TableHead className="pr-4">
                    <span className="sr-only">Actions</span>
                  </TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {customers.data.items.map((c) => (
                  <TableRow key={c.id}>
                    <TableCell className="pl-4">
                      <div className="flex items-center gap-3">
                        <CustomerAvatar name={c.name} />
                        <span className="min-w-0">
                          <span className="block font-medium">{c.name}</span>
                          {/* On small screens the email column is hidden, so it shows under the name. */}
                          <span className="block truncate text-xs text-muted-foreground md:hidden">{c.email}</span>
                        </span>
                      </div>
                    </TableCell>
                    <TableCell className="hidden text-muted-foreground md:table-cell">{c.email}</TableCell>
                    <TableCell className="hidden sm:table-cell">{countryName(c.countryCode)}</TableCell>
                    <TableCell className="hidden text-muted-foreground lg:table-cell">{formatDateTime(c.createdAt)}</TableCell>
                    <TableCell className="pr-4">
                      <div className="flex justify-end gap-1">
                        <Button asChild variant="ghost" size="sm">
                          <Link to={`/orders?customerId=${c.id}`}>Orders</Link>
                        </Button>
                        <Button asChild variant="outline" size="sm">
                          <Link to={`/orders/new?customerId=${c.id}`}>
                            <Package />
                            New order
                          </Link>
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
            <Pagination label="Customer" {...customers.data} onPageChange={(p) => update({ page: String(p) })} />
          </>
        )}
      </Card>
    </section>
  )
}

function NoCustomers({ search }: { search: string }) {
  return (
    <EmptyState
      icon={UserRound}
      title={search ? `No customers match “${search}”.` : 'No customers yet. Create the first one.'}
      description={search ? 'Search matches the start of a name or email.' : 'Customers need a SADC country before they can order.'}
    />
  )
}
