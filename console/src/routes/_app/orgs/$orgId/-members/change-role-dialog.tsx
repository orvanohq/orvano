import { useState } from 'react'

import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { FieldLegend, FieldSet } from '@/components/ui/field'
import { FormAlert } from '@/components/ui/form-alert'
import { describeError } from '@/lib/errors'
import type { Member, OrgRole } from '@orvano/console-client'

import { RoleRadioGroup } from './role-radio-group'
import { memberName } from './member-name'

/**
 * Change a member's role (spec 0008, AC-19): the same radio group as the invite, Save disabled until
 * the role differs. Demoting yourself from owner warns first. A refusal, such as `last_owner`, shows
 * the server's message in the alert and keeps the dialog open.
 */
export function ChangeRoleDialog({
  member,
  isSelf,
  open,
  onOpenChange,
  onSave,
}: {
  member: Member
  isSelf: boolean
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Saves the role; throw to keep the dialog open with the error in its alert. */
  onSave: (role: OrgRole) => Promise<void>
}) {
  const [role, setRole] = useState<OrgRole>(member.role)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const demotingSelf = isSelf && member.role === 'owner' && role !== 'owner'

  const reset = () => {
    setRole(member.role)
    setError(null)
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) reset()
        onOpenChange(next)
      }}
    >
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Change role</DialogTitle>
          <DialogDescription>
            {isSelf ? 'Your role' : `The role of ${memberName(member)}`} in this org. It takes
            effect on the next thing they do.
          </DialogDescription>
        </DialogHeader>
        <form
          noValidate
          className="flex flex-col gap-(--stack)"
          onSubmit={(event) => {
            event.preventDefault()
            if (role === member.role) return
            setBusy(true)
            setError(null)
            onSave(role)
              .then(
                () => {
                  onOpenChange(false)
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
          {error === null ? null : <FormAlert title="Couldn't change the role">{error}</FormAlert>}
          <FieldSet>
            <FieldLegend id="change-role-label" variant="label">
              Role
            </FieldLegend>
            <RoleRadioGroup
              id="change-role"
              labelledBy="change-role-label"
              value={role}
              onChange={setRole}
            />
          </FieldSet>
          {demotingSelf ? (
            <FormAlert variant="warning" title="You'll lose owner rights in this org">
              You can't manage members or the org afterwards unless another owner makes you an owner
              again.
            </FormAlert>
          ) : null}
          <DialogFooter>
            <DialogClose render={<Button variant="outline" />}>Cancel</DialogClose>
            <Button
              type="submit"
              loading={busy}
              disabledReason={role === member.role ? 'Choose a different role' : undefined}
            >
              Save
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
