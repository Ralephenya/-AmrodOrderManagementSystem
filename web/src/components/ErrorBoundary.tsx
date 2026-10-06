import { Component, type ErrorInfo, type ReactNode } from 'react'
import { isRouteErrorResponse, Link, useRouteError } from 'react-router'
import { Compass } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { EmptyState } from './EmptyState'
import { ProblemAlert } from './ProblemAlert'

interface Props {
  children: ReactNode
}

interface State {
  error: unknown
}

/** Last line of defence for render errors outside the router (providers, layout). */
export class ErrorBoundary extends Component<Props, State> {
  override state: State = { error: null }

  static getDerivedStateFromError(error: unknown): State {
    return { error }
  }

  override componentDidCatch(error: unknown, info: ErrorInfo) {
    console.error('Unhandled UI error', error, info.componentStack)
  }

  override render() {
    if (this.state.error) {
      return (
        <main className="mx-auto max-w-xl px-4 py-16">
          <h1 className="mb-4 text-2xl font-semibold tracking-tight">Something went wrong</h1>
          <ProblemAlert error={this.state.error} onRetry={() => window.location.reload()} />
        </main>
      )
    }
    return this.props.children
  }
}

/** Router error element: unknown URLs and errors thrown while rendering a route. */
export function RouteErrorPage() {
  const error = useRouteError()
  const notFound = error === undefined || (isRouteErrorResponse(error) && error.status === 404)
  return (
    <section aria-labelledby="route-error-title" className="mx-auto max-w-md">
      <EmptyState
        icon={Compass}
        titleId="route-error-title"
        className="py-20"
        title={notFound ? "We couldn't find that page" : 'Something went wrong'}
        description={notFound ? 'The link may be out of date, or the page has moved.' : 'Please try again in a moment.'}
      >
        <div className="mt-6 grid w-full justify-items-center gap-4">
          {!notFound && <ProblemAlert error={error} />}
          <Button asChild>
            <Link to="/orders">Go to orders</Link>
          </Button>
        </div>
      </EmptyState>
    </section>
  )
}
