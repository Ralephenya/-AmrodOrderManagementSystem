import { Toaster as Sonner } from 'sonner'
import { useTheme } from '@/lib/theme'

/** sonner's toaster, themed to match. sonner announces toasts through its own aria-live region. */
export function Toaster() {
  const { theme } = useTheme()
  return (
    <Sonner
      theme={theme}
      position="bottom-right"
      closeButton
      richColors
      toastOptions={{ classNames: { toast: 'font-sans' } }}
    />
  )
}
