import type { ComponentType, ReactNode } from 'react'
import { cn } from '@/lib/utils'

interface Props {
  icon: ComponentType<{ className?: string }>
  title: ReactNode
  description?: ReactNode
  /** Renders the title as the page's h1 (with this id) instead of a plain line. */
  titleId?: string
  className?: string
  children?: ReactNode
}

/** An icon in a circle, a title and a hint: the "nothing here" state for lists and error pages. */
export function EmptyState({ icon: Icon, title, description, titleId, className, children }: Props) {
  return (
    <div className={cn('flex flex-col items-center px-6 py-16 text-center', className)}>
      <span className="mb-4 flex size-12 items-center justify-center rounded-full bg-muted">
        <Icon className="size-6 text-muted-foreground" aria-hidden="true" />
      </span>
      {titleId ? (
        <h1 id={titleId} className="text-2xl font-semibold tracking-tight">
          {title}
        </h1>
      ) : (
        <p className="font-medium">{title}</p>
      )}
      {description && <p className="mt-1 text-sm text-muted-foreground">{description}</p>}
      {children}
    </div>
  )
}
