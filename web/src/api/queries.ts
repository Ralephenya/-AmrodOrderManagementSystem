import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from './client'
import { ApiError, NetworkError } from './problem'
import type { Country, CreateCustomerRequest, CreateOrderRequest, Order, OrderStatus } from './types'

export const queryKeys = {
  countries: ['countries'] as const,
  customers: (params: CustomerListParams) => ['customers', 'list', params] as const,
  customer: (id: string) => ['customers', 'detail', id] as const,
  orders: (params: OrderListParams) => ['orders', 'list', params] as const,
  order: (id: string) => ['orders', 'detail', id] as const,
  topSpenders: (currency: string, days: number, top: number) => ['reports', 'top-spenders', currency, days, top] as const,
}

export interface CustomerListParams {
  search?: string
  page: number
  pageSize: number
  sort?: string
}

export interface OrderListParams {
  customerId?: string
  status?: OrderStatus
  page: number
  pageSize: number
  sort?: string
}

/** Countries and their permitted currencies. Static reference data, so it is fetched once per session. */
export function useCountries() {
  return useQuery({
    queryKey: queryKeys.countries,
    queryFn: async ({ signal }) => (await unwrap(api.GET('/api/v1/reference/countries', { signal }))).data,
    staleTime: Infinity,
    gcTime: Infinity,
  })
}

export function findCountry(countries: readonly Country[] | undefined, code: string | undefined) {
  return code ? countries?.find((c) => c.code === code) : undefined
}

export function useCustomers(params: CustomerListParams, options: { enabled?: boolean } = {}) {
  return useQuery({
    queryKey: queryKeys.customers(params),
    enabled: options.enabled ?? true,
    queryFn: async ({ signal }) =>
      (await unwrap(api.GET('/api/v1/customers', { params: { query: withoutEmpty(params) }, signal }))).data,
    placeholderData: keepPreviousData, // keep the old page on screen while the next one loads
  })
}

export function useCustomer(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.customer(id ?? ''),
    queryFn: async ({ signal }) =>
      (await unwrap(api.GET('/api/v1/customers/{id}', { params: { path: { id: id! } }, signal }))).data,
    enabled: Boolean(id),
  })
}

export function useCreateCustomer() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: async (body: CreateCustomerRequest) => (await unwrap(api.POST('/api/v1/customers', { body }))).data,
    onSuccess: (customer) => {
      client.setQueryData(queryKeys.customer(customer.id), customer)
      return client.invalidateQueries({ queryKey: ['customers', 'list'] })
    },
  })
}

export function useOrders(params: OrderListParams) {
  return useQuery({
    queryKey: queryKeys.orders(params),
    queryFn: async ({ signal }) =>
      (await unwrap(api.GET('/api/v1/orders', { params: { query: withoutEmpty(params) }, signal }))).data,
    placeholderData: keepPreviousData,
  })
}

/** An order plus its ETag, which status changes send back as If-Match. */
export interface VersionedOrder {
  order: Order
  etag: string | null
}

/** Paid but not yet allocated: the worker allocates stock shortly after payment, and only then can it ship. */
export function isAwaitingAllocation(order: Order): boolean {
  return order.status === 'Paid' && !order.allocatedAt
}

// How often to re-check a paid order while the worker allocates and fulfils it.
export const ALLOCATION_POLL_MS = 3000

export function useOrder(id: string) {
  return useQuery({
    queryKey: queryKeys.order(id),
    queryFn: async ({ signal }): Promise<VersionedOrder> => {
      const { data, response } = await unwrap(api.GET('/api/v1/orders/{id}', { params: { path: { id } }, signal }))
      return { order: data, etag: response.headers.get('ETag') }
    },
    // While the order is Paid the worker is still at work (it allocates stock, then fulfils the order itself),
    // so keep checking until it moves on and the page updates by itself.
    refetchInterval: (query) => (query.state.data?.order.status === 'Paid' ? ALLOCATION_POLL_MS : false),
  })
}

export function useCreateOrder() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: async (body: CreateOrderRequest): Promise<VersionedOrder> => {
      const { data, response } = await unwrap(api.POST('/api/v1/orders', { body }))
      return { order: data, etag: response.headers.get('ETag') }
    },
    onSuccess: (created) => {
      client.setQueryData(queryKeys.order(created.order.id), created)
      return client.invalidateQueries({ queryKey: ['orders', 'list'] })
    },
  })
}

export interface ChangeStatusInput {
  status: OrderStatus
  /** One key per click; a retry of the same click must reuse it. */
  idempotencyKey: string
  /** The ETag the user was looking at. If the order changed since, the API answers 412. */
  etag: string | null
}

export function useChangeOrderStatus(id: string) {
  const client = useQueryClient()
  return useMutation({
    // Safe to retry when the request may never have arrived: a retry re-sends the same variables, so the same
    // Idempotency-Key, and the API replays the first result instead of applying the change twice.
    retry: (failureCount, error) => failureCount < 2 && error instanceof NetworkError,
    mutationFn: async ({ status, idempotencyKey, etag }: ChangeStatusInput): Promise<VersionedOrder> => {
      const { data, response } = await unwrap(
        api.PUT('/api/v1/orders/{id}/status', {
          params: {
            path: { id },
            header: { 'Idempotency-Key': idempotencyKey },
          },
          headers: etag ? { 'If-Match': etag } : {},
          body: { status },
        }),
      )
      return { order: data, etag: response.headers.get('ETag') }
    },
    onSuccess: (changed) => {
      client.setQueryData(queryKeys.order(id), changed)
      return client.invalidateQueries({ queryKey: ['orders', 'list'] })
    },
    onError: (error) => {
      // 412/409: the order changed under us. Reload it so the user sees the current state and legal actions.
      if (error instanceof ApiError && (error.status === 412 || error.status === 409)) {
        return client.invalidateQueries({ queryKey: queryKeys.order(id) })
      }
    },
  })
}

/** Customers ranked by Paid + Fulfilled spend in one currency (Orders.Admin). Never summed across currencies. */
export function useTopSpenders(currency: string, days = 90, top = 5) {
  return useQuery({
    queryKey: queryKeys.topSpenders(currency, days, top),
    queryFn: async ({ signal }) =>
      (await unwrap(api.GET('/api/v1/reports/top-spenders', { params: { query: { currency, days, top } }, signal }))).data,
    placeholderData: keepPreviousData,
  })
}

/** Drop empty strings and undefined so they don't reach the query string as `search=`. */
function withoutEmpty<T extends object>(params: T): T {
  return Object.fromEntries(Object.entries(params).filter(([, v]) => v !== undefined && v !== '')) as T
}
