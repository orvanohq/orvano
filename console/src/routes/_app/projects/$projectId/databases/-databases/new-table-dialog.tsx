import { useForm } from '@tanstack/react-form'
import { Plus, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { z } from 'zod'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import {
  Field,
  FieldDescription,
  FieldError,
  FieldLabel,
  FieldLegend,
  FieldSet,
} from '@/components/ui/field'
import { FormAlert } from '@/components/ui/form-alert'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { describeError } from '@/lib/errors'
import type { ColumnInput, CreateTableRequest } from '@orvano/console-client'

import {
  creatableTypes,
  defaultChoices,
  defaultLabels,
  fieldOfProblem,
  nameProblem,
  typeLabels,
  valueHint,
  type CreatableType,
  type DefaultChoice,
} from './column-types'

/** One row of the column builder. */
interface ColumnDraft {
  name: string
  type: CreatableType
  required: boolean
  unique: boolean
  defaultChoice: DefaultChoice
  defaultValue: string
}

interface TableDraft {
  name: string
  columns: ColumnDraft[]
}

const emptyDraft: TableDraft = { name: '', columns: [] }

type ColumnField = keyof ColumnDraft

/** A column field's form name: TanStack Form names array items by their index. */
function at<F extends ColumnField>(index: number, field: F) {
  return `columns[${String(index)}].${field}` as `columns[${number}].${F}`
}

const newColumn = (): ColumnDraft => ({
  name: '',
  type: 'text',
  required: false,
  unique: false,
  defaultChoice: 'none',
  defaultValue: '',
})

const schema = z
  .object({ name: z.string(), columns: z.array(z.custom<ColumnDraft>()) })
  .superRefine((value, context) => {
    const tableProblem = nameProblem(value.name.trim(), false)
    if (tableProblem !== undefined) {
      context.addIssue({ code: 'custom', path: ['name'], message: tableProblem })
    }
    const seen = new Set<string>()
    value.columns.forEach((column, index) => {
      const name = column.name.trim()
      const problem =
        nameProblem(name, true) ?? (seen.has(name) ? 'Another column has this name' : undefined)
      if (problem !== undefined) {
        context.addIssue({ code: 'custom', path: ['columns', index, 'name'], message: problem })
      }
      seen.add(name)
    })
  })

/** The create request for a draft: defaults by kind, a fixed value only for `value`. */
export function createRequest(draft: TableDraft): CreateTableRequest {
  return {
    name: draft.name.trim(),
    columns: draft.columns.map((column): ColumnInput => {
      const input: ColumnInput = {
        name: column.name.trim(),
        type: column.type,
        required: column.required,
        unique: column.unique,
      }
      if (column.defaultChoice === 'none') return input
      return {
        ...input,
        default: {
          kind: column.defaultChoice,
          value: column.defaultChoice === 'value' ? column.defaultValue : null,
        },
      }
    }),
  }
}

const systemColumns = [
  { name: 'id', type: 'UUID', note: 'Primary key, a time ordered UUID' },
  { name: 'created_at', type: 'Timestamp', note: 'Set when the row is created' },
  { name: 'updated_at', type: 'Timestamp', note: 'Moves forward on every change' },
] as const

const typeItems = creatableTypes.map((type) => ({ value: type, label: typeLabels[type] }))

/**
 * New table (spec 0015, AC-32): the name, the three system columns shown fixed, and a column builder
 * (name, type, Required, Unique, and a default by kind). Field problems show under their field,
 * including the ones the server finds (a reserved word, a value that doesn't cast); anything else
 * shows in the form alert. `onCreate` resolves with the new table's name once the dialog may close.
 */
export function NewTableDialog({
  open,
  onOpenChange,
  onCreate,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  onCreate: (request: CreateTableRequest) => Promise<void>
}) {
  const [serverError, setServerError] = useState<string | null>(null)
  // Server problems by field name (`name`, `columns[2].default`), cleared when that field changes.
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})
  const clearFieldError = (key: string) => {
    setFieldErrors((current) => {
      if (!(key in current)) return current
      return Object.fromEntries(Object.entries(current).filter(([name]) => name !== key))
    })
  }

  const form = useForm({
    defaultValues: emptyDraft,
    validators: { onSubmit: schema },
    onSubmit: async ({ value }) => {
      setServerError(null)
      setFieldErrors({})
      try {
        await onCreate(createRequest(value))
        onOpenChange(false)
        form.reset()
      } catch (error) {
        const { message } = describeError(error)
        const field = fieldOfProblem(message)
        if (field === undefined) {
          setServerError(message)
        } else {
          const key =
            field.column === null ? 'name' : `columns[${String(field.column)}].${field.field}`
          setFieldErrors({ [key]: field.message })
        }
      }
    },
  })

  /** One column of the builder, a fieldset labelled by its number. */
  const columnRow = (index: number, onRemove: () => void) => {
    const prefix = `column-${String(index)}`
    const errorsOf = (key: string, own: readonly unknown[]) => [
      ...own.map((error) => ({ message: typeof error === 'string' ? error : messageOf(error) })),
      ...(key in fieldErrors ? [{ message: fieldErrors[key] }] : []),
    ]

    return (
      <fieldset key={index} className="flex flex-col gap-3 rounded-md border p-3">
        <legend className="px-1 text-sm font-medium">Column {index + 1}</legend>
        <div className="grid gap-3 sm:grid-cols-2">
          <form.Field name={at(index, 'name')}>
            {(field) => {
              const id = `${prefix}-name`
              const errors = errorsOf(`columns[${String(index)}].name`, field.state.meta.errors)
              const invalid = errors.length > 0
              return (
                <Field data-invalid={invalid || undefined}>
                  <FieldLabel htmlFor={id}>Name</FieldLabel>
                  <Input
                    id={id}
                    autoComplete="off"
                    spellCheck={false}
                    className="font-mono"
                    value={field.state.value}
                    aria-invalid={invalid || undefined}
                    aria-describedby={invalid ? `${id}-error` : undefined}
                    onBlur={field.handleBlur}
                    onChange={(event) => {
                      clearFieldError(`columns[${String(index)}].name`)
                      field.handleChange(event.target.value)
                    }}
                  />
                  {invalid ? <FieldError id={`${id}-error`} errors={errors} /> : null}
                </Field>
              )
            }}
          </form.Field>
          <form.Field name={at(index, 'type')}>
            {(field) => {
              const id = `${prefix}-type`
              const errors = errorsOf(`columns[${String(index)}].type`, [])
              const invalid = errors.length > 0
              return (
                <Field data-invalid={invalid || undefined}>
                  <FieldLabel htmlFor={id}>Type</FieldLabel>
                  <Select
                    items={typeItems}
                    value={field.state.value}
                    onValueChange={(next) => {
                      if (next === null) return
                      clearFieldError(`columns[${String(index)}].type`)
                      field.handleChange(next)
                      // A default kind the new type doesn't take goes back to none.
                      const choice = form.getFieldValue(at(index, 'defaultChoice'))
                      if (!defaultChoices(next).includes(choice)) {
                        form.setFieldValue(at(index, 'defaultChoice'), 'none')
                      }
                    }}
                  >
                    <SelectTrigger id={id} className="w-full">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {creatableTypes.map((type) => (
                        <SelectItem key={type} value={type}>
                          {typeLabels[type]}{' '}
                          <span className="font-mono text-muted-foreground">{type}</span>
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                  {invalid ? <FieldError id={`${id}-error`} errors={errors} /> : null}
                </Field>
              )
            }}
          </form.Field>
        </div>
        <div className="flex flex-wrap items-center gap-x-6 gap-y-2">
          <form.Field name={at(index, 'required')}>
            {(field) => (
              <label className="flex items-center gap-2 text-sm">
                <Checkbox
                  checked={field.state.value}
                  onCheckedChange={(checked) => {
                    field.handleChange(checked)
                  }}
                />
                Required
              </label>
            )}
          </form.Field>
          <form.Field name={at(index, 'unique')}>
            {(field) => (
              <label className="flex items-center gap-2 text-sm">
                <Checkbox
                  checked={field.state.value}
                  onCheckedChange={(checked) => {
                    field.handleChange(checked)
                  }}
                />
                Unique
              </label>
            )}
          </form.Field>
        </div>
        <form.Subscribe selector={(state) => state.values.columns[index]?.type ?? 'text'}>
          {(type) => (
            <div className="grid gap-3 sm:grid-cols-2">
              <form.Field name={at(index, 'defaultChoice')}>
                {(field) => {
                  const id = `${prefix}-default`
                  const choices = defaultChoices(type)
                  return (
                    <Field>
                      <FieldLabel htmlFor={id}>Default</FieldLabel>
                      <Select
                        items={choices.map((choice) => ({
                          value: choice,
                          label: defaultLabels[choice],
                        }))}
                        value={field.state.value}
                        onValueChange={(next) => {
                          if (next === null) return
                          clearFieldError(`columns[${String(index)}].default`)
                          field.handleChange(next)
                        }}
                      >
                        <SelectTrigger id={id} className="w-full">
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          {choices.map((choice) => (
                            <SelectItem key={choice} value={choice}>
                              {defaultLabels[choice]}
                            </SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    </Field>
                  )
                }}
              </form.Field>
              <form.Subscribe selector={(state) => state.values.columns[index]?.defaultChoice}>
                {(choice) =>
                  choice === 'value' ? (
                    <form.Field name={at(index, 'defaultValue')}>
                      {(field) => {
                        const id = `${prefix}-value`
                        const errors = errorsOf(`columns[${String(index)}].default`, [])
                        const invalid = errors.length > 0
                        return (
                          <Field data-invalid={invalid || undefined}>
                            <FieldLabel htmlFor={id}>Default value</FieldLabel>
                            <Input
                              id={id}
                              autoComplete="off"
                              spellCheck={false}
                              className="font-mono"
                              value={field.state.value}
                              aria-invalid={invalid || undefined}
                              aria-describedby={`${id}-hint${invalid ? ` ${id}-error` : ''}`}
                              onChange={(event) => {
                                clearFieldError(`columns[${String(index)}].default`)
                                field.handleChange(event.target.value)
                              }}
                            />
                            <FieldDescription id={`${id}-hint`}>{valueHint(type)}</FieldDescription>
                            {invalid ? <FieldError id={`${id}-error`} errors={errors} /> : null}
                          </Field>
                        )
                      }}
                    </form.Field>
                  ) : null
                }
              </form.Subscribe>
            </div>
          )}
        </form.Subscribe>
        <div>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            aria-label={`Remove column ${String(index + 1)}`}
            onClick={onRemove}
          >
            <Trash2 aria-hidden />
            Remove
          </Button>
        </div>
      </fieldset>
    )
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        onOpenChange(next)
        setServerError(null)
        setFieldErrors({})
        if (!next) form.reset()
      }}
    >
      <DialogContent className="max-h-[90dvh] overflow-y-auto sm:max-w-3xl">
        <DialogHeader>
          <DialogTitle>New table</DialogTitle>
          <DialogDescription>
            A real Postgres table. Server SDKs can use it as soon as it exists.
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
            <FormAlert title="Couldn't create the table">{serverError}</FormAlert>
          )}
          <form.Field name="name">
            {(field) => {
              const id = 'table-name'
              const errors = [
                ...field.state.meta.errors,
                ...('name' in fieldErrors ? [{ message: fieldErrors.name }] : []),
              ]
              const invalid = errors.length > 0
              return (
                <Field data-invalid={invalid || undefined}>
                  <FieldLabel htmlFor={id}>Name</FieldLabel>
                  <Input
                    id={id}
                    autoComplete="off"
                    spellCheck={false}
                    className="font-mono"
                    placeholder="tasks"
                    value={field.state.value}
                    aria-invalid={invalid || undefined}
                    aria-describedby={`${id}-hint${invalid ? ` ${id}-error` : ''}`}
                    onBlur={field.handleBlur}
                    onChange={(event) => {
                      clearFieldError('name')
                      field.handleChange(event.target.value)
                    }}
                  />
                  <FieldDescription id={`${id}-hint`}>
                    Lowercase letters, digits, and _, starting with a letter.
                  </FieldDescription>
                  {invalid ? <FieldError id={`${id}-error`} errors={errors} /> : null}
                </Field>
              )
            }}
          </form.Field>

          <FieldSet>
            <FieldLegend variant="label">Columns</FieldLegend>
            <ul className="flex flex-col gap-1 text-sm" aria-label="System columns">
              {systemColumns.map((column) => (
                <li key={column.name} className="flex flex-wrap items-center gap-2">
                  <code className="font-mono">{column.name}</code>
                  <span className="text-muted-foreground">{column.type}</span>
                  <Badge>System</Badge>
                  <span className="text-muted-foreground">{column.note}</span>
                </li>
              ))}
            </ul>
            <form.Field name="columns" mode="array">
              {(columns) => (
                <div className="flex flex-col gap-3">
                  {columns.state.value.map((_, index) =>
                    columnRow(index, () => {
                      setFieldErrors({})
                      columns.removeValue(index)
                    }),
                  )}
                  <div>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={() => {
                        columns.pushValue(newColumn())
                      }}
                    >
                      <Plus aria-hidden />
                      Add column
                    </Button>
                  </div>
                </div>
              )}
            </form.Field>
          </FieldSet>

          <DialogFooter>
            <DialogClose render={<Button variant="outline" />}>Cancel</DialogClose>
            <form.Subscribe selector={(state) => state.isSubmitting}>
              {(submitting) => (
                <Button type="submit" loading={submitting}>
                  Create table
                </Button>
              )}
            </form.Subscribe>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

/** The message of a validator error, whatever shape the schema gave it. */
function messageOf(error: unknown): string {
  if (typeof error === 'object' && error !== null && 'message' in error) {
    const { message } = error
    if (typeof message === 'string') return message
  }
  return 'Check this field'
}
