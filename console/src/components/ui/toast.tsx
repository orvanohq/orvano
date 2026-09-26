import { Toast } from '@base-ui/react/toast'
import { CircleCheck, Info, OctagonX, TriangleAlert, X } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

/** The one toast manager; call `toastManager.add(...)` through `@/lib/toast`, never directly. */
export const toastManager = Toast.createToastManager()

const icons = {
  success: <CircleCheck aria-hidden className="mt-0.5 size-(--icon) shrink-0 text-success-text" />,
  error: <OctagonX aria-hidden className="mt-0.5 size-(--icon) shrink-0 text-danger-text" />,
  warning: (
    <TriangleAlert aria-hidden className="mt-0.5 size-(--icon) shrink-0 text-warning-text" />
  ),
  info: <Info aria-hidden className="mt-0.5 size-(--icon) shrink-0 text-muted-foreground" />,
}

function ToastList() {
  const { toasts } = Toast.useToastManager()
  return toasts.map((toast) => (
    <Toast.Root
      key={toast.id}
      toast={toast}
      className={cn(
        'pointer-events-auto flex w-full items-start gap-3 rounded-lg border border-border bg-popover p-3 text-popover-foreground shadow-(--shadow-overlay)',
        'transition-[opacity,transform] duration-150 ease-(--ease) data-ending-style:opacity-0 data-starting-style:translate-y-2 data-starting-style:opacity-0',
      )}
    >
      {toast.type === undefined ? null : icons[toast.type as keyof typeof icons]}
      <div className="flex min-w-0 flex-1 flex-col gap-0.5">
        <Toast.Title className="font-medium" />
        <Toast.Description className="text-muted-foreground" />
        {toast.actionProps === undefined ? null : (
          <Toast.Action render={<Button variant="outline" size="sm" className="mt-2 w-fit" />} />
        )}
      </div>
      <Toast.Close
        aria-label="Dismiss"
        aria-hidden={false}
        render={<Button variant="ghost" size="icon" className="-mt-1 -mr-1" />}
      >
        <X aria-hidden />
      </Toast.Close>
    </Toast.Root>
  ))
}

/**
 * The toast region: at most 3 visible, F6 moves focus into it, Escape dismisses the focused toast,
 * and a toast stops its timer while hovered or focused. Mount once, at the root.
 */
export function Toaster() {
  return (
    <Toast.Provider toastManager={toastManager} limit={3} timeout={5000}>
      <Toast.Portal>
        <Toast.Viewport className="pointer-events-none fixed right-4 bottom-4 z-(--z-toast) flex w-80 max-w-[calc(100vw-2rem)] flex-col gap-2 outline-none">
          <ToastList />
        </Toast.Viewport>
      </Toast.Portal>
    </Toast.Provider>
  )
}
