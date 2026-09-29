import { useRef, useState, type ReactElement } from 'react'

import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from '@/components/ui/alert-dialog'
import { Button } from '@/components/ui/button'
import { FormAlert } from '@/components/ui/form-alert'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { describeError } from '@/lib/errors'

interface ConfirmDialogProps {
  /** The element that opens the dialog, usually a `Button`. Omit it to control the dialog with `open`. */
  trigger?: ReactElement
  /** Controls the dialog from outside, for example from a menu item that has already closed. */
  open?: boolean
  onOpenChange?: (open: boolean) => void
  title: string
  description: string
  /** A verb phrase, never "OK": "Delete project". */
  confirmLabel: string
  destructive?: boolean
  /** Makes the person type this name before the confirm button works (for deleting things). */
  requireName?: string
  /** Runs the action. Throw to keep the dialog open: the error's message shows in its alert. */
  onConfirm: () => void | Promise<void>
}

/**
 * A confirmation before a risky action. Focus starts on Cancel and Escape cancels. With
 * `requireName` the confirm button stays disabled until the exact name is typed. The dialog closes
 * once `onConfirm` finishes; if it throws, the dialog stays open and shows the error in its alert.
 * Without a `trigger`, `open` and `onOpenChange` control it, and focus returns to what had it before
 * it opened (a row menu's button).
 */
export function ConfirmDialog({
  trigger,
  open: controlledOpen,
  onOpenChange,
  title,
  description,
  confirmLabel,
  destructive = false,
  requireName,
  onConfirm,
}: ConfirmDialogProps) {
  const cancelRef = useRef<HTMLButtonElement | null>(null)
  const [uncontrolledOpen, setUncontrolledOpen] = useState(false)
  const open = controlledOpen ?? uncontrolledOpen
  const setOpen = (next: boolean) => {
    setUncontrolledOpen(next)
    onOpenChange?.(next)
  }
  const [typed, setTyped] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const nameOk = requireName === undefined || typed === requireName

  return (
    <AlertDialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        setTyped('')
        setError(null)
      }}
    >
      {trigger === undefined ? null : <AlertDialogTrigger render={trigger} />}
      <AlertDialogContent initialFocus={cancelRef}>
        <AlertDialogHeader>
          <AlertDialogTitle>{title}</AlertDialogTitle>
          <AlertDialogDescription>{description}</AlertDialogDescription>
        </AlertDialogHeader>
        {error === null ? null : (
          <FormAlert title={`Couldn't ${confirmLabel.toLowerCase()}`}>{error}</FormAlert>
        )}
        {requireName === undefined ? null : (
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="confirm-name">
              Type <code className="font-mono">{requireName}</code> to confirm
            </Label>
            <Input
              id="confirm-name"
              value={typed}
              autoComplete="off"
              onChange={(event) => {
                setTyped(event.target.value)
              }}
            />
          </div>
        )}
        <AlertDialogFooter>
          <AlertDialogCancel ref={cancelRef}>Cancel</AlertDialogCancel>
          <AlertDialogAction
            render={<Button variant={destructive ? 'destructive' : 'primary'} loading={busy} />}
            disabled={!nameOk}
            onClick={() => {
              setBusy(true)
              setError(null)
              void Promise.resolve()
                .then(onConfirm)
                .then(
                  () => {
                    setOpen(false)
                    setTyped('')
                  },
                  (failure: unknown) => {
                    setError(describeError(failure).message)
                  },
                )
                .finally(() => {
                  setBusy(false)
                })
            }}
          >
            {confirmLabel}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}
