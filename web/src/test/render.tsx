import { QueryClient } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { StrictMode } from 'react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter } from 'react-router'
import { App } from '../app/App'
import { routes } from '../app/routes'

/**
 * Renders the real app (routes, providers) at `path`, with a fresh cache and no retries. StrictMode on, as in
 * main.tsx, so double-invoked effects behave the same as in the browser.
 */
export function renderRoute(path: string) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: Infinity }, mutations: { retry: false } },
  })
  const router = createMemoryRouter(routes, { initialEntries: [path] })
  const user = userEvent.setup()
  const view = render(
    <StrictMode>
      <App queryClient={queryClient} router={router} />
    </StrictMode>,
  )
  return { ...view, user, router, queryClient }
}
