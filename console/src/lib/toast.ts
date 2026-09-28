import { toastManager } from '@/components/ui/toast'
import { describeError } from '@/lib/errors'

/** A button inside a toast, for example "Restore" after a delete (spec 0007, AC-8). */
export interface ToastAction {
  label: string
  onClick: () => void
}

/**
 * A success toast: disappears after 5 seconds, paused while hovered or focused (AC-21). An
 * `action` adds a button; pressing it runs `onClick` and closes the toast.
 */
export function notifySuccess(title: string, description?: string, action?: ToastAction): void {
  const id: string = toastManager.add({
    type: 'success',
    title,
    description,
    timeout: 5000,
    actionProps:
      action === undefined
        ? undefined
        : {
            children: action.label,
            onClick: () => {
              toastManager.close(id)
              action.onClick()
            },
          },
  })
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
