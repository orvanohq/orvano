import { useForm } from '@tanstack/react-form'
import { useState } from 'react'
import { z } from 'zod'

import { Button } from '@/components/ui/button'
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'
import { FormAlert } from '@/components/ui/form-alert'
import { Input } from '@/components/ui/input'
import { describeError } from '@/lib/errors'
import { nameSchema } from '@/shell/name-dialog'

const schema = z.object({ name: nameSchema })

/**
 * The name form of an org or project's General section (spec 0007, AC-3 and AC-7). With a
 * `disabledReason` the field is read only and Save explains why. A server error shows in the form
 * alert; an unchanged name sends nothing.
 */
export function RenameForm({
  id,
  name,
  disabledReason,
  errorTitle,
  onRename,
}: {
  /** Prefix for the field's ID, unique on the page. */
  id: string
  /** The current name. */
  name: string
  disabledReason: string | undefined
  errorTitle: string
  /** Receives the trimmed new name; throws to show the error in the form alert. */
  onRename: (name: string) => Promise<void>
}) {
  const [serverError, setServerError] = useState<string | null>(null)
  const form = useForm({
    defaultValues: { name },
    validators: { onSubmit: schema },
    onSubmit: async ({ value }) => {
      const next = value.name.trim()
      setServerError(null)
      if (next === name) return
      try {
        await onRename(next)
        form.reset({ name: next })
      } catch (error) {
        setServerError(describeError(error).message)
      }
    },
  })
  const fieldId = `${id}-name`
  const readOnly = disabledReason !== undefined

  return (
    <form
      noValidate
      className="flex max-w-xl flex-col gap-(--stack)"
      onSubmit={(event) => {
        event.preventDefault()
        void form.handleSubmit()
      }}
    >
      {serverError === null ? null : <FormAlert title={errorTitle}>{serverError}</FormAlert>}
      <form.Field name="name">
        {(field) => {
          const invalid = field.state.meta.errors.length > 0
          const describedBy = [
            readOnly ? `${fieldId}-reason` : undefined,
            invalid ? `${fieldId}-error` : undefined,
          ].filter((part) => part !== undefined)
          return (
            <Field data-invalid={invalid || undefined}>
              <FieldLabel htmlFor={fieldId}>Name</FieldLabel>
              <Input
                id={fieldId}
                autoComplete="off"
                readOnly={readOnly}
                value={field.state.value}
                aria-invalid={invalid || undefined}
                aria-describedby={describedBy.length === 0 ? undefined : describedBy.join(' ')}
                onBlur={field.handleBlur}
                onChange={(event) => {
                  field.handleChange(event.target.value)
                }}
              />
              {readOnly ? (
                <FieldDescription id={`${fieldId}-reason`}>{disabledReason}</FieldDescription>
              ) : null}
              {invalid ? (
                <FieldError id={`${fieldId}-error`} errors={field.state.meta.errors} />
              ) : null}
            </Field>
          )
        }}
      </form.Field>
      <form.Subscribe selector={(state) => state.isSubmitting}>
        {(submitting) => (
          <Button
            type="submit"
            className="w-fit"
            loading={submitting}
            disabledReason={disabledReason}
          >
            Save
          </Button>
        )}
      </form.Subscribe>
    </form>
  )
}
