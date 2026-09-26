import { toastManager } from '@/components/ui/toast'
import { describeError } from '@/lib/errors'

/** A success toast: disappears after 5 seconds, paused while hovered or focused (AC-21). */
export function notifySuccess(title: string, description?: string): void {
  toastManager.add({ type: 'success', title, description, timeout: 5000 })
}

/** A failed action: stays until dismissed, with the error's message (AC-21). */
export function notifyError(title: string, error?: unknown): void {
  toastManager.add({
    type: 'error',
    title,
    description: error === undefined ? undefined : describeError(error).message,
    timeout: 0,
  })
}
