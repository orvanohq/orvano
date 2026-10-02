import { useForm } from '@tanstack/react-form'
import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { KeyRound, MonitorSmartphone, UsersRound } from 'lucide-react'
import { useState } from 'react'
import { z } from 'zod'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
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
  DialogTrigger,
} from '@/components/ui/dialog'
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { Field, FieldError, FieldLabel } from '@/components/ui/field'
import { FormAlert } from '@/components/ui/form-alert'
import { Input } from '@/components/ui/input'
import { RelativeTime } from '@/shell/relative-time'
import { authErrorMessage } from '@/routes/-auth/auth-form'
import type { Session, SigningKey, User, UserStatus } from '@orvano/console-client'

import { VerifiedBadge, sessionMethodLabel } from './email-parts'

/** A user's status as a word with its dot; every status shows its word (spec 0005). */
export function UserStatusBadge({ status }: { status: UserStatus }) {
  return (
    <Badge variant="status" tone={status === 'active' ? 'success' : 'danger'}>
      {status === 'active' ? 'Active' : 'Blocked'}
    </Badge>
  )
}

/** The Users table: email (the link to the user), name, status, and dates. */
export function userColumns(projectId: string): ColumnDef<User>[] {
  return [
    {
      accessorKey: 'email',
      header: 'Email',
      cell: ({ row }) => (
        <Link
          to="/projects/$projectId/users/$userId"
          params={{ projectId, userId: row.original.id }}
          className="font-medium text-link hover:underline"
        >
          {row.original.email ?? row.original.id}
        </Link>
      ),
    },
    { accessorKey: 'name', header: 'Name', cell: ({ row }) => row.original.name ?? '' },
    {
      accessorKey: 'emailVerified',
      header: 'Verified',
      cell: ({ row }) => <VerifiedBadge verified={row.original.emailVerified} />,
    },
    {
      accessorKey: 'status',
      header: 'Status',
      cell: ({ row }) => <UserStatusBadge status={row.original.status} />,
    },
    {
      accessorKey: 'createdAt',
      header: 'Signed up',
      cell: ({ row }) => <RelativeTime iso={row.original.createdAt} />,
    },
    {
      accessorKey: 'lastSignInAt',
      header: 'Last sign in',
      cell: ({ row }) =>
        row.original.lastSignInAt === null ? (
          'Never'
        ) : (
          <RelativeTime iso={row.original.lastSignInAt} />
        ),
    },
  ]
}

/** The empty Users table: nobody has signed up yet, or nobody matches the search. */
export function NoUsers({ searching }: { searching: boolean }) {
  return (
    <Empty className="border">
      <EmptyHeader>
        <EmptyMedia variant="icon">
          <UsersRound aria-hidden />
        </EmptyMedia>
        <EmptyTitle>{searching ? 'No matching users' : 'No users yet'}</EmptyTitle>
        <EmptyDescription>
          {searching
            ? 'No user matches the search and filter.'
            : 'People who sign up to your app, or that you create here, appear here.'}
        </EmptyDescription>
      </EmptyHeader>
    </Empty>
  )
}

/**
 * A user's active sessions: when they signed in and were last active, the device, and where from.
 * `endReason` is set when you may not end sessions (a viewer); the End buttons then explain why.
 */
export function SessionsTable({
  sessions,
  loading,
  error,
  onRetry,
  hasMore,
  onLoadMore,
  endReason,
  onEnd,
}: {
  sessions: readonly Session[]
  loading: boolean
  error: unknown
  onRetry: () => void
  hasMore: boolean
  onLoadMore: () => void
  endReason: string | undefined
  onEnd: (session: Session) => Promise<void>
}) {
  const columns: ColumnDef<Session>[] = [
    {
      accessorKey: 'createdAt',
      header: 'Signed in',
      cell: ({ row }) => <RelativeTime iso={row.original.createdAt} />,
    },
    {
      accessorKey: 'lastRefreshedAt',
      header: 'Last active',
      cell: ({ row }) => <RelativeTime iso={row.original.lastRefreshedAt} />,
    },
    {
      accessorKey: 'method',
      header: 'Method',
      cell: ({ row }) => sessionMethodLabel(row.original.method),
    },
    {
      accessorKey: 'userAgent',
      header: 'Device',
      cell: ({ row }) => (
        <span className="line-clamp-1 max-w-xs break-all">
          {row.original.userAgent ?? 'Unknown'}
        </span>
      ),
    },
    { accessorKey: 'sdk', header: 'SDK', cell: ({ row }) => row.original.sdk ?? '' },
    {
      accessorKey: 'ipAddress',
      header: 'IP address',
      cell: ({ row }) => row.original.ipAddress ?? '',
    },
    {
      id: 'end',
      header: () => <span className="sr-only">Actions</span>,
      enableSorting: false,
      cell: ({ row }) => (
        <ConfirmDialog
          trigger={
            <Button variant="outline" size="sm" disabledReason={endReason}>
              End session
            </Button>
          }
          title="End this session?"
          description="The device signs out: its refresh token stops working now, and its access token within 15 minutes at most."
          confirmLabel="End session"
          destructive
          onConfirm={() => onEnd(row.original)}
        />
      ),
    },
  ]
  return (
    <DataTable
      label="Sessions"
      columns={columns}
      data={sessions}
      loading={loading}
      error={error}
      onRetry={onRetry}
      hasMore={hasMore}
      onLoadMore={onLoadMore}
      empty={
        <Empty className="border">
          <EmptyHeader>
            <EmptyMedia variant="icon">
              <MonitorSmartphone aria-hidden />
            </EmptyMedia>
            <EmptyTitle>No active sessions</EmptyTitle>
            <EmptyDescription>This user is signed in nowhere.</EmptyDescription>
          </EmptyHeader>
        </Empty>
      }
    />
  )
}

const createSchema = z.object({
  email: z
    .string()
    .trim()
    // abort: an empty email fails the regex too, and should show one message, not two
    .min(1, { error: 'Enter an email.', abort: true })
    .max(320)
    .regex(/^[^\s@]+@[^\s@]+$/, 'Enter an email address.'),
  password: z.string().min(8, 'Use at least 8 characters.').max(256, 'Use at most 256 characters.'),
  name: z.string().trim().max(256, 'Use at most 256 characters.'),
})

/** "Create user": email, password, and an optional name. No session is created for them. */
export function CreateUserDialog({
  disabledReason,
  onCreate,
}: {
  disabledReason: string | undefined
  onCreate: (values: { email: string; password: string; name: string }) => Promise<void>
}) {
  const [open, setOpen] = useState(false)
  const [serverError, setServerError] = useState<string | null>(null)
  const form = useForm({
    defaultValues: { email: '', password: '', name: '' },
    validators: { onSubmit: createSchema },
    onSubmit: async ({ value }) => {
      setServerError(null)
      try {
        await onCreate(value)
        setOpen(false)
        form.reset()
      } catch (error) {
        setServerError(authErrorMessage(error))
      }
    },
  })
  const fields = [
    { name: 'email', label: 'Email', type: 'email', autoComplete: 'off' },
    { name: 'password', label: 'Password', type: 'password', autoComplete: 'new-password' },
    { name: 'name', label: 'Name (optional)', type: 'text', autoComplete: 'off' },
  ] as const

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        setServerError(null)
      }}
    >
      <DialogTrigger render={<Button disabledReason={disabledReason} />}>Create user</DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Create a user</DialogTitle>
          <DialogDescription>
            They can sign in to your app with this email and password.
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
            <FormAlert title="Couldn't create the user">{serverError}</FormAlert>
          )}
          {fields.map((spec) => (
            <form.Field key={spec.name} name={spec.name}>
              {(field) => {
                const invalid = field.state.meta.errors.length > 0
                const id = `create-user-${spec.name}`
                return (
                  <Field data-invalid={invalid || undefined}>
                    <FieldLabel htmlFor={id}>{spec.label}</FieldLabel>
                    <Input
                      id={id}
                      type={spec.type}
                      autoComplete={spec.autoComplete}
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
          ))}
          <DialogFooter>
            <DialogClose render={<Button variant="outline" />}>Cancel</DialogClose>
            <form.Subscribe selector={(state) => state.isSubmitting}>
              {(submitting) => (
                <Button type="submit" loading={submitting}>
                  Create user
                </Button>
              )}
            </form.Subscribe>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

/**
 * The project's token signing keys (spec 0004, AC-22): key IDs, status, and dates for every role;
 * rotation for owners only, behind a confirmation.
 */
export function SigningKeysPanel({
  keys,
  loading,
  error,
  onRetry,
  rotateReason,
  onRotate,
}: {
  keys: readonly SigningKey[]
  loading: boolean
  error: unknown
  onRetry: () => void
  rotateReason: string | undefined
  onRotate: () => Promise<void>
}) {
  const columns: ColumnDef<SigningKey>[] = [
    {
      accessorKey: 'id',
      header: 'Key ID',
      cell: ({ row }) => <code className="font-mono">{row.original.id}</code>,
    },
    {
      accessorKey: 'status',
      header: 'Status',
      cell: ({ row }) => (
        <Badge variant="status" tone={row.original.status === 'active' ? 'success' : 'warning'}>
          {row.original.status === 'active' ? 'Active' : 'Retiring'}
        </Badge>
      ),
    },
    {
      accessorKey: 'createdAt',
      header: 'Created',
      cell: ({ row }) => <RelativeTime iso={row.original.createdAt} />,
    },
    {
      accessorKey: 'retireAfter',
      header: 'Leaves',
      cell: ({ row }) =>
        row.original.retireAfter === null ? '' : <RelativeTime iso={row.original.retireAfter} />,
    },
  ]
  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2>Token signing keys</h2>
        </CardTitle>
        <CardDescription>
          Access tokens of this project are signed with the active key. Rotating makes a new key
          sign from now on; the old one keeps verifying the tokens it signed for 24 hours, then
          leaves.
        </CardDescription>
        <CardAction>
          <ConfirmDialog
            trigger={
              <Button variant="outline" disabledReason={rotateReason}>
                <KeyRound aria-hidden />
                Rotate key
              </Button>
            }
            title="Rotate the signing key?"
            description="A new key signs every token from now on. Tokens the current key signed keep working until they expire."
            confirmLabel="Rotate key"
            onConfirm={onRotate}
          />
        </CardAction>
      </CardHeader>
      <CardContent>
        <DataTable
          label="Signing keys"
          columns={columns}
          data={keys}
          loading={loading}
          error={error}
          onRetry={onRetry}
          empty={
            <p className="text-muted-foreground">No key yet: the first sign in creates one.</p>
          }
        />
      </CardContent>
    </Card>
  )
}
