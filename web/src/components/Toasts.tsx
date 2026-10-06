import { Toaster as Sonner } from 'sonner'
import { useTheme } from '@/lib/theme'

/**
 * sonner's toaster, themed to match. sonner announces toasts through its own aria-live region. Plain (not
 * `richColors`) toasts: rich colours put green text on pale green, below 4.5:1 contrast; the icon carries the tone.
 */
export function Toaster() {
  const { theme } = useTheme()
  return (
    <Sonner
      theme={theme}
      position="bottom-right"
      closeButton
      toastOptions={{ classNames: { toast: 'font-sans' } }}
    />
  )
}
