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
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

interface ConfirmDialogProps {
  /** The element that opens the dialog, usually a `Button`. */
  trigger: ReactElement
  title: string
  description: string
  /** A verb phrase, never "OK": "Delete project". */
  confirmLabel: string
  destructive?: boolean
  /** Makes the person type this name before the confirm button works (for deleting things). */
  requireName?: string
  onConfirm: () => void | Promise<void>
}

/**
 * A confirmation before a risky action. Focus starts on Cancel and Escape cancels. With
 * `requireName` the confirm button stays disabled until the exact name is typed.
 */
export function ConfirmDialog({
  trigger,
  title,
  description,
  confirmLabel,
  destructive = false,
  requireName,
  onConfirm,
}: ConfirmDialogProps) {
  const cancelRef = useRef<HTMLButtonElement | null>(null)
  const [typed, setTyped] = useState('')
  const [busy, setBusy] = useState(false)
  const nameOk = requireName === undefined || typed === requireName

  return (
    <AlertDialog
      onOpenChange={() => {
        setTyped('')
      }}
    >
      <AlertDialogTrigger render={trigger} />
      <AlertDialogContent initialFocus={cancelRef}>
        <AlertDialogHeader>
          <AlertDialogTitle>{title}</AlertDialogTitle>
          <AlertDialogDescription>{description}</AlertDialogDescription>
        </AlertDialogHeader>
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
              void Promise.resolve(onConfirm()).finally(() => {
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
