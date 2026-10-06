import { toast } from 'sonner'

export interface ToastApi {
  success(message: string): void
  error(message: string): void
}

const api: ToastApi = {
  success: (message) => toast.success(message),
  error: (message) => toast.error(message),
}

/** The app's toast calls go through here, so the toast library stays a detail of one file. */
export function useToast(): ToastApi {
  return api
}
