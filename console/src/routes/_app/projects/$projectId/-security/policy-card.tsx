import type { ReactNode } from 'react'

import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { FormAlert } from '@/components/ui/form-alert'

/**
 * One card of the Security page (spec 0014, AC-34): its own `h2`, a form that saves only its own
 * fields, the error a save got when no field owns it, and a Save button. With `readOnlyReason` (a
 * viewer) the card still shows every value, its controls are disabled by the caller, and Save says
 * why it can't run.
 */
export function PolicyCard({
  id,
  title,
  description,
  readOnlyReason,
  alert,
  saving,
  onSubmit,
  children,
}: {
  /** Prefix for the card's element IDs, such as `passwords`. */
  id: string
  title: string
  description: ReactNode
  readOnlyReason: string | undefined
  /** The message of a refused save no field owns (text, or text with a link), or null. */
  alert: ReactNode
  saving: boolean
  onSubmit: () => void
  children: ReactNode
}) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 id={`${id}-heading`}>{title}</h2>
        </CardTitle>
        <CardDescription>{description}</CardDescription>
      </CardHeader>
      <CardContent>
        <form
          className="flex flex-col gap-4"
          aria-labelledby={`${id}-heading`}
          noValidate
          onSubmit={(event) => {
            event.preventDefault()
            onSubmit()
          }}
        >
          {alert === null ? null : <FormAlert title="Couldn't save">{alert}</FormAlert>}
          {children}
          <div className="flex justify-end">
            <Button type="submit" disabledReason={readOnlyReason} loading={saving}>
              Save
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  )
}
