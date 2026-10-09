import type { ColumnDef } from '@tanstack/react-table'
import { browserPasskeys } from '@orvano/console-client'
import type { Passkey } from '@orvano/console-client'
import { useState } from 'react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DataTable } from '@/components/ui/data-table'
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Field, FieldError, FieldLabel } from '@/components/ui/field'
import { FormAlert } from '@/components/ui/form-alert'
import { Input } from '@/components/ui/input'
import { consoleApi } from '@/lib/console-client'
import { describeError } from '@/lib/errors'
import { formatDateTime } from '@/lib/format'
import { notifyError, notifySuccess } from '@/lib/toast'
import { passkeyCancelled, passkeyErrorMessage } from '@/routes/-auth/passkeys'
import { SettingsSection } from '@/shell/settings-section'

type Run = <T>(action: (password?: string) => Promise<T>) => Promise<{ value: T } | null>

/**
 * The passkeys section of the account Security page (spec 0013, AC-42): your passkeys with when
 * each was added and last used and whether it syncs, Add a passkey (the browser's prompt), and
 * Rename and Remove per passkey. Adding and removing go through `run`, the page's step up.
 */
export function PasskeysSection({
  passkeys,
  loading,
  error,
  onRetry,
  supported,
  run,
  onChanged,
}: {
  passkeys: readonly Passkey[]
  loading: boolean
  error: unknown
  onRetry: () => void
  /** Whether this browser can make and use passkeys. */
  supported: boolean
  run: Run
  onChanged: () => Promise<void>
}) {
  const [adding, setAdding] = useState(false)
  const [addError, setAddError] = useState<string | null>(null)
  const [renaming, setRenaming] = useState<Passkey | null>(null)

  const add = async () => {
    setAdding(true)
    setAddError(null)
    try {
      const made = await run(async (password) => {
        const api = consoleApi().consoleAccount
        const registration = await api.createPasskeyRegistration({ password })
        const credential = await browserPasskeys.create(registration.options)
        return api.completePasskeyRegistration({
          challengeId: registration.challengeId,
          credential,
        })
      })
      if (made === null) return
      notifySuccess('Passkey added', made.value.name)
      await onChanged()
    } catch (failure) {
      if (!passkeyCancelled(failure)) setAddError(passkeyErrorMessage(failure))
    } finally {
      setAdding(false)
    }
  }

  const remove = async (passkey: Passkey) => {
    try {
      if ((await run(() => consoleApi().consoleAccount.deletePasskey(passkey.id))) === null) return
      notifySuccess('Passkey removed', passkey.name)
      await onChanged()
    } catch (failure) {
      notifyError("Couldn't remove the passkey", failure)
    }
  }

  const columns: ColumnDef<Passkey>[] = [
    { accessorKey: 'name', header: 'Name', cell: ({ row }) => row.original.name },
    {
      accessorKey: 'createdAt',
      header: 'Added',
      cell: ({ row }) => formatDateTime(row.original.createdAt),
    },
    {
      accessorKey: 'lastUsedAt',
      header: 'Last used',
      cell: ({ row }) =>
        row.original.lastUsedAt === null ? 'Never' : formatDateTime(row.original.lastUsedAt),
    },
    {
      accessorKey: 'synced',
      header: 'Kind',
      cell: ({ row }) => (row.original.synced ? 'Synced' : 'Device bound'),
    },
    {
      accessorKey: 'active',
      header: 'State',
      cell: ({ row }) => (
        <Badge variant="status" tone={row.original.active ? 'success' : 'neutral'}>
          {row.original.active ? 'Active' : 'Inactive'}
        </Badge>
      ),
    },
    {
      id: 'actions',
      header: () => <span className="sr-only">Actions</span>,
      cell: ({ row }) => (
        <div className="flex justify-end gap-2">
          <Button
            variant="outline"
            size="sm"
            aria-label={`Rename passkey ${row.original.name}`}
            onClick={() => {
              setRenaming(row.original)
            }}
          >
            Rename
          </Button>
          <ConfirmDialog
            trigger={
              <Button
                variant="outline"
                size="sm"
                aria-label={`Remove passkey ${row.original.name}`}
              >
                Remove
              </Button>
            }
            title={`Remove ${row.original.name}?`}
            description="You can't sign in with this passkey anymore. Your sessions stay."
            confirmLabel="Remove passkey"
            destructive
            onConfirm={() => {
              void remove(row.original)
            }}
          />
        </div>
      ),
    },
  ]

  return (
    <SettingsSection
      title="Passkeys"
      description="Sign in with your face, fingerprint, or device PIN, with no password. A passkey also counts as a second factor."
    >
      {addError === null ? null : (
        <FormAlert title="Couldn't add the passkey">{addError}</FormAlert>
      )}
      <DataTable
        label="Passkeys"
        columns={columns}
        data={passkeys}
        loading={loading}
        error={error}
        onRetry={onRetry}
        empty={<p className="p-4 text-muted-foreground">No passkey yet.</p>}
      />
      <Button
        className="self-start"
        loading={adding}
        disabledReason={supported ? undefined : "This browser can't make passkeys."}
        onClick={() => {
          void add()
        }}
      >
        Add a passkey
      </Button>
      {renaming === null ? null : (
        <RenameDialog
          key={renaming.id}
          passkey={renaming}
          onClose={() => {
            setRenaming(null)
          }}
          onSaved={onChanged}
        />
      )}
    </SettingsSection>
  )
}

/** Renames one passkey: 1 to 64 characters. */
function RenameDialog({
  passkey,
  onClose,
  onSaved,
}: {
  passkey: Passkey
  onClose: () => void
  onSaved: () => Promise<void>
}) {
  const [name, setName] = useState(passkey.name)
  const [fieldError, setFieldError] = useState<string | null>(null)
  const [alert, setAlert] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const save = async () => {
    const trimmed = name.trim()
    if (trimmed.length < 1 || trimmed.length > 64) {
      setFieldError('Use 1 to 64 characters.')
      return
    }
    setFieldError(null)
    setAlert(null)
    setBusy(true)
    try {
      const renamed = await consoleApi().consoleAccount.updatePasskey(passkey.id, { name: trimmed })
      notifySuccess('Passkey renamed', renamed.name)
      await onSaved()
      onClose()
    } catch (failure) {
      setAlert(describeError(failure).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open) onClose()
      }}
    >
      <DialogContent>
        <form
          noValidate
          className="flex flex-col gap-4"
          onSubmit={(event) => {
            event.preventDefault()
            void save()
          }}
        >
          <DialogHeader>
            <DialogTitle>Rename passkey</DialogTitle>
            <DialogDescription>A name that tells your passkeys apart.</DialogDescription>
          </DialogHeader>
          {alert === null ? null : <FormAlert title="Couldn't rename">{alert}</FormAlert>}
          <Field data-invalid={fieldError !== null || undefined}>
            <FieldLabel htmlFor="passkey-name">Name</FieldLabel>
            <Input
              id="passkey-name"
              value={name}
              autoComplete="off"
              aria-invalid={fieldError !== null || undefined}
              aria-describedby={fieldError === null ? undefined : 'passkey-name-error'}
              onChange={(event) => {
                setName(event.target.value)
              }}
            />
            {fieldError === null ? null : (
              <FieldError id="passkey-name-error" errors={[{ message: fieldError }]} />
            )}
          </Field>
          <DialogFooter>
            <DialogClose render={<Button variant="outline" />}>Cancel</DialogClose>
            <Button type="submit" loading={busy}>
              Save
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
