import { cn } from '@/lib/utils'

const TINTS = [
  'bg-chart-1/20 ring-1 ring-inset ring-chart-1/30',
  'bg-chart-2/20 ring-1 ring-inset ring-chart-2/30',
  'bg-chart-3/20 ring-1 ring-inset ring-chart-3/30',
  'bg-chart-4/20 ring-1 ring-inset ring-chart-4/30',
  'bg-chart-5/20 ring-1 ring-inset ring-chart-5/30',
]

/**
 * Initials on a tint chosen from the name, so the same customer always gets the same colour. The initials stay in
 * the foreground colour (coloured text on its own tint fails 4.5:1 contrast); the tint and ring carry the hue.
 */
export function CustomerAvatar({ name, size = 'md' }: { name: string; size?: 'sm' | 'md' }) {
  const initials =
    name
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((w) => w[0]!.toUpperCase())
      .join('') || '?'
  const hash = [...name].reduce((h, c) => (h * 31 + c.charCodeAt(0)) >>> 0, 7)
  return (
    <span
      aria-hidden="true"
      className={cn(
        'flex shrink-0 items-center justify-center rounded-full font-semibold text-foreground',
        size === 'sm' ? 'size-6 text-[10px]' : 'size-8 text-xs',
        TINTS[hash % TINTS.length],
      )}
    >
      {initials}
    </span>
  )
}
