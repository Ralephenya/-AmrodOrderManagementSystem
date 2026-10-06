import { Navigate, type RouteObject } from 'react-router'
import { RouteErrorPage } from '@/components/ErrorBoundary'
import { Layout } from '@/components/Layout'

// Each page is its own chunk, loaded when first visited. The shell (layout, router, data layer) stays in the main
// bundle; the charting library only loads with the dashboard.
export const routes: RouteObject[] = [
  {
    element: <Layout />,
    errorElement: <RouteErrorPage />,
    children: [
      { index: true, element: <Navigate to="/dashboard" replace /> },
      {
        path: 'dashboard',
        lazy: async () => ({ Component: (await import('@/features/dashboard/DashboardPage')).DashboardPage }),
      },
      {
        path: 'customers',
        lazy: async () => ({ Component: (await import('@/features/customers/CustomersPage')).CustomersPage }),
      },
      {
        path: 'customers/new',
        lazy: async () => ({ Component: (await import('@/features/customers/CreateCustomerPage')).CreateCustomerPage }),
      },
      { path: 'orders', lazy: async () => ({ Component: (await import('@/features/orders/OrdersPage')).OrdersPage }) },
      {
        path: 'orders/new',
        lazy: async () => ({ Component: (await import('@/features/orders/CreateOrderPage')).CreateOrderPage }),
      },
      {
        path: 'orders/:id',
        lazy: async () => ({ Component: (await import('@/features/orders/OrderDetailsPage')).OrderDetailsPage }),
      },
      { path: '*', element: <RouteErrorPage /> },
    ],
  },
]
