import { useForm } from '@tanstack/react-form'
import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { z } from 'zod'

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
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'
import { FormAlert } from '@/components/ui/form-alert'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { projectClient } from '@/lib/console-client'
import { describeError } from '@/lib/errors'
import { platformTypeOrder, platformTypes, reduceWebIdentifier } from '@/lib/platform-types'
import { keys } from '@/lib/queries'
import { notifySuccess } from '@/lib/toast'
import { nameSchema } from '@/shell/name-dialog'
import type { PlatformType } from '@orvano/console-client'

import { PlatformTypeLabel } from './columns'

const schema = z
  .object({ type: z.custom<PlatformType>(), name: nameSchema, identifier: z.string() })
  .superRefine((value, context) => {
    const problem = platformTypes[value.type].check(value.identifier.trim())
    if (problem !== undefined) {
      context.addIssue({ code: 'custom', path: ['identifier'], message: problem })
    }
  })

interface FormValues {
  type: PlatformType
  name: string
  identifier: string
}

const defaults: FormValues = { type: 'web', name: '', identifier: '' }

const typeItems = platformTypeOrder.map((type) => ({
  value: type,
  label: platformTypes[type].label,
}))

/**
 * Add platform (spec 0007, AC-18 and AC-19): Type first, then Name and an Identifier whose label,
 * placeholder, hint, and rule follow the type. A pasted web URL is reduced to its hostname when the
 * field loses focus. A server 400 (a duplicate, say) shows in the form alert.
 */
export function PlatformDialog({
  projectId,
  open,
  onOpenChange,
}: {
  projectId: string
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  const queryClient = useQueryClient()
  const [serverError, setServerError] = useState<string | null>(null)
  const form = useForm({
    defaultValues: defaults,
    validators: { onSubmit: schema },
    onSubmit: async ({ value }) => {
      setServerError(null)
      try {
        const platform = await projectClient(projectId).consolePlatforms.create({
          type: value.type,
          name: value.name.trim(),
          identifier: value.identifier.trim(),
        })
        await queryClient.invalidateQueries({ queryKey: keys.platforms(projectId) })
        notifySuccess('Platform added', platform.name)
        onOpenChange(false)
        form.reset()
      } catch (error) {
        setServerError(describeError(error).message)
      }
    },
  })

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        onOpenChange(next)
        setServerError(null)
        if (!next) form.reset()
      }}
    >
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Add a platform</DialogTitle>
          <DialogDescription>
            Only the platforms you add can call this project from a browser or an app.
          </DialogDescription>
        </DialogHeader>
        <form
          noValidate
          className="flex flex-col gap-(--stack)"
          onSubmit={(event) => {
            event.preventDefault()
            void form.handleSubmit()
          }}
        >
          {serverError === null ? null : (
            <FormAlert title="Couldn't add the platform">{serverError}</FormAlert>
          )}
          <p className="text-small text-muted-foreground">
            Flutter apps: add one platform for each target you ship (Web, Android, iOS, and so on).
          </p>
          <form.Field name="type">
            {(field) => (
              <Field>
                <FieldLabel htmlFor="platform-type">Type</FieldLabel>
                <Select
                  items={typeItems}
                  value={field.state.value}
                  onValueChange={(next) => {
                    if (next !== null) field.handleChange(next)
                  }}
                >
                  <SelectTrigger id="platform-type" className="w-48">
                    <SelectValue>
                      {(value: PlatformType) => <PlatformTypeLabel type={value} />}
                    </SelectValue>
                  </SelectTrigger>
                  <SelectContent>
                    {platformTypeOrder.map((type) => (
                      <SelectItem key={type} value={type}>
                        <PlatformTypeLabel type={type} />
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </Field>
            )}
          </form.Field>
          <form.Field name="name">
            {(field) => {
              const invalid = field.state.meta.errors.length > 0
              const id = 'platform-name'
              return (
                <Field data-invalid={invalid || undefined}>
                  <FieldLabel htmlFor={id}>Name</FieldLabel>
                  <Input
                    id={id}
                    autoComplete="off"
                    value={field.state.value}
                    aria-invalid={invalid || undefined}
                    aria-describedby={invalid ? `${id}-error` : undefined}
                    onBlur={field.handleBlur}
                    onChange={(event) => {
                      field.handleChange(event.target.value)
                    }}
                  />
                  {invalid ? (
                    <FieldError id={`${id}-error`} errors={field.state.meta.errors} />
                  ) : null}
                </Field>
              )
            }}
          </form.Field>
          <form.Subscribe selector={(state) => state.values.type}>
            {(type) => (
              <form.Field name="identifier">
                {(field) => {
                  const invalid = field.state.meta.errors.length > 0
                  const id = 'platform-identifier'
                  const info = platformTypes[type]
                  return (
                    <Field data-invalid={invalid || undefined}>
                      <FieldLabel htmlFor={id}>{info.identifierLabel}</FieldLabel>
                      <Input
                        id={id}
                        autoComplete="off"
                        spellCheck={false}
                        className="font-mono"
                        placeholder={info.placeholder}
                        value={field.state.value}
                        aria-invalid={invalid || undefined}
                        aria-describedby={`${id}-hint${invalid ? ` ${id}-error` : ''}`}
                        onBlur={() => {
                          // A pasted URL keeps only its hostname (AC-19).
                          if (type === 'web') {
                            field.handleChange(reduceWebIdentifier(field.state.value))
                          }
                          field.handleBlur()
                        }}
                        onChange={(event) => {
                          field.handleChange(event.target.value)
                        }}
                      />
                      <FieldDescription id={`${id}-hint`}>{info.hint}</FieldDescription>
                      {invalid ? (
                        <FieldError id={`${id}-error`} errors={field.state.meta.errors} />
                      ) : null}
                    </Field>
                  )
                }}
              </form.Field>
            )}
          </form.Subscribe>
          <DialogFooter>
            <DialogClose render={<Button variant="outline" />}>Cancel</DialogClose>
            <form.Subscribe selector={(state) => state.isSubmitting}>
              {(submitting) => (
                <Button type="submit" loading={submitting}>
                  Add platform
                </Button>
              )}
            </form.Subscribe>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
