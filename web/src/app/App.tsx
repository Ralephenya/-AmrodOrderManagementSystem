import { QueryClientProvider, type QueryClient } from '@tanstack/react-query'
import type { ComponentProps } from 'react'
import { RouterProvider } from 'react-router'
import { ErrorBoundary } from '@/components/ErrorBoundary'
import { ThemeProvider } from '@/components/ThemeProvider'
import { Toaster } from '@/components/Toasts'
import { TooltipProvider } from '@/components/ui/tooltip'

interface Props {
  queryClient: QueryClient
  /** A browser router in the app, a memory router in tests. */
  router: ComponentProps<typeof RouterProvider>['router']
}

export function App({ queryClient, router }: Props) {
  return (
    <ThemeProvider>
      <ErrorBoundary>
        <QueryClientProvider client={queryClient}>
          <TooltipProvider delayDuration={200}>
            <RouterProvider router={router} />
          </TooltipProvider>
        </QueryClientProvider>
      </ErrorBoundary>
      <Toaster />
    </ThemeProvider>
  )
}
