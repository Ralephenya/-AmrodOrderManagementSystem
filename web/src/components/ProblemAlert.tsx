import { AlertCircle, RotateCw } from 'lucide-react'
import { describeError } from '@/api/problem'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'

/** An inline, friendly error panel. Shows the API's correlation ID so users can quote it to support. */
export function ProblemAlert({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const { title, detail, reference } = describeError(error)
  return (
    <Alert variant="destructive" className="mb-4">
      <AlertCircle />
      <AlertTitle>{title}</AlertTitle>
      <AlertDescription>
        {detail && <p>{detail}</p>}
        {reference && (
          <p className="text-xs text-muted-foreground">
            Reference: <code className="font-mono">{reference}</code>
          </p>
        )}
        {onRetry && (
          <Button variant="outline" size="sm" className="mt-2" onClick={onRetry}>
            <RotateCw />
            Try again
          </Button>
        )}
      </AlertDescription>
    </Alert>
  )
}
