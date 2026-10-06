import type { components } from './schema'

type Schemas = components['schemas']

export type Country = Schemas['CountryResponse']
export type Currency = Schemas['CurrencyResponse']
export type Customer = Schemas['CustomerResponse']
export type CreateCustomerRequest = Schemas['CreateCustomerRequest']
export type CustomerPage = Schemas['CustomerResponsePagedResult']
export type Order = Schemas['OrderResponse']
export type OrderLine = Schemas['OrderLineItemResponse']
export type OrderStatus = Schemas['OrderStatus']
export type OrderSummary = Schemas['OrderSummaryResponse']
export type OrderPage = Schemas['OrderSummaryResponsePagedResult']
export type CreateOrderRequest = Schemas['CreateOrderRequest']
export type ProblemDetails = Schemas['ProblemDetails']
export type TopSpenders = Schemas['TopSpendersResponse']

export const ORDER_STATUSES: readonly OrderStatus[] = ['Pending', 'Paid', 'Fulfilled', 'Cancelled']
