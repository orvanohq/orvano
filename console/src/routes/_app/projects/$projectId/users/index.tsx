import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { Search } from 'lucide-react'
import { useEffect, useState } from 'react'

import { DataTable } from '@/components/ui/data-table'
import { InputGroup, InputGroupAddon, InputGroupInput } from '@/components/ui/input-group'
import { Label } from '@/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { projectClient } from '@/lib/console-client'
import { usePageTitle } from '@/lib/page-title'
import { keys, projectQuery, usersQuery } from '@/lib/queries'
import { roleReason, useOrgRole } from '@/lib/roles'
import { notifySuccess } from '@/lib/toast'
import { meetsRole } from '@/shell/nav'
import { PageHeading } from '@/shell/page-heading'

import { CreateUserDialog, NoUsers, userColumns } from '../-users/parts'

/** The verification filter's choices; the URL keeps `emailVerified=true` or `false` (spec 0010, AC-22). */
const verificationOptions = [
  { value: 'all', label: 'All' },
  { value: 'verified', label: 'Verified' },
  { value: 'unverified', label: 'Unverified' },
] as const

type VerificationChoice = (typeof verificationOptions)[number]['value']

/** The MFA filter's choices; the URL keeps `mfa=on` or `off` (spec 0013, AC-44). */
const mfaOptions = [
  { value: 'all', label: 'All' },
  { value: 'on', label: 'On' },
  { value: 'off', label: 'Off' },
] as const

/** The kind filter's choices; the URL keeps `anonymous=true` or `false` (spec 0014, AC-32). */
const kindOptions = [
  { value: 'all', label: 'All' },
  { value: 'permanent', label: 'Permanent' },
  { value: 'guest', label: 'Guests' },
] as const

interface UsersSearch {
  emailVerified?: boolean
  mfa?: 'on' | 'off'
  anonymous?: boolean
}

/** A search with only the filters that are set, so the URL never carries an empty one. */
function filters(search: {
  emailVerified?: boolean | undefined
  mfa?: 'on' | 'off' | undefined
  anonymous?: boolean | undefined
}): UsersSearch {
  return {
    ...(search.emailVerified === undefined ? {} : { emailVerified: search.emailVerified }),
    ...(search.mfa === undefined ? {} : { mfa: search.mfa }),
    ...(search.anonymous === undefined ? {} : { anonymous: search.anonymous }),
  }
}

export const Route = createFileRoute('/_app/projects/$projectId/users/')({
  validateSearch: (search: Record<string, unknown>): UsersSearch =>
    filters({
      ...(typeof search.emailVerified === 'boolean' ? { emailVerified: search.emailVerified } : {}),
      ...(search.mfa === 'on' || search.mfa === 'off' ? { mfa: search.mfa } : {}),
      ...(typeof search.anonymous === 'boolean' ? { anonymous: search.anonymous } : {}),
    }),
  component: UsersPage,
})

/**
 * The project's users (spec 0004, AC-29): newest first, paged, with a prefix search on email, and
 * verification (spec 0010, AC-22), MFA (spec 0013, AC-44), and guest (spec 0014, AC-32) filters
 * kept in the URL. Owners and developers create users; viewers see the button with the reason it is
 * not theirs.
 */
function UsersPage() {
  const { projectId } = Route.useParams()
  const { emailVerified, mfa, anonymous } = Route.useSearch()
  const navigate = Route.useNavigate()
  const choice: VerificationChoice =
    emailVerified === undefined ? 'all' : emailVerified ? 'verified' : 'unverified'
  const project = useQuery(projectQuery(projectId)).data
  usePageTitle('Users', project?.name)
  const role = useOrgRole()
  const queryClient = useQueryClient()
  const [typed, setTyped] = useState('')
  const [email, setEmail] = useState('')
  useEffect(() => {
    // Search after a short pause in typing, not on every key.
    const timer = setTimeout(() => {
      setEmail(typed.trim())
    }, 250)
    return () => {
      clearTimeout(timer)
    }
  }, [typed])
  const users = useInfiniteQuery(usersQuery(projectId, email, emailVerified, 25, mfa, anonymous))
  const rows = users.data?.pages.flatMap((page) => page.items) ?? []

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-6">
      <div className="flex flex-wrap items-center gap-3">
        <PageHeading>Users</PageHeading>
        <div className="ml-auto">
          <CreateUserDialog
            disabledReason={meetsRole(role, 'developer') ? undefined : roleReason('developer')}
            onCreate={async ({ email: newEmail, password, name }) => {
              await projectClient(projectId).consoleUsers.create({
                email: newEmail,
                password,
                name: name === '' ? null : name,
              })
              await queryClient.invalidateQueries({ queryKey: keys.users(projectId) })
              notifySuccess('User created', newEmail)
            }}
          />
        </div>
      </div>
      <div className="flex flex-wrap items-end gap-4">
        <div className="flex w-full max-w-sm flex-col gap-1.5">
          <Label htmlFor="users-search">Search by email</Label>
          <InputGroup>
            <InputGroupAddon>
              <Search aria-hidden />
            </InputGroupAddon>
            <InputGroupInput
              id="users-search"
              type="search"
              placeholder="Starts with…"
              value={typed}
              onChange={(event) => {
                setTyped(event.target.value)
              }}
            />
          </InputGroup>
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="users-verification">Verification</Label>
          <Select
            items={verificationOptions}
            value={choice}
            onValueChange={(next) => {
              if (next === null) return
              void navigate({
                search: (prev) =>
                  filters({
                    ...prev,
                    emailVerified: next === 'all' ? undefined : next === 'verified',
                  }),
                replace: true,
              })
            }}
          >
            <SelectTrigger id="users-verification" className="w-40">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {verificationOptions.map((option) => (
                <SelectItem key={option.value} value={option.value}>
                  {option.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="users-mfa">MFA</Label>
          <Select
            items={mfaOptions}
            value={mfa ?? 'all'}
            onValueChange={(next) => {
              if (next === null) return
              void navigate({
                search: (prev) =>
                  filters({ ...prev, mfa: next === 'on' || next === 'off' ? next : undefined }),
                replace: true,
              })
            }}
          >
            <SelectTrigger id="users-mfa" className="w-32">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {mfaOptions.map((option) => (
                <SelectItem key={option.value} value={option.value}>
                  {option.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="users-kind">Kind</Label>
          <Select
            items={kindOptions}
            value={anonymous === undefined ? 'all' : anonymous ? 'guest' : 'permanent'}
            onValueChange={(next) => {
              if (next === null) return
              void navigate({
                search: (prev) =>
                  filters({ ...prev, anonymous: next === 'all' ? undefined : next === 'guest' }),
                replace: true,
              })
            }}
          >
            <SelectTrigger id="users-kind" className="w-36">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {kindOptions.map((option) => (
                <SelectItem key={option.value} value={option.value}>
                  {option.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </div>
      <DataTable
        label="Users"
        columns={userColumns(projectId)}
        data={rows}
        loading={users.isPending}
        error={users.isError ? users.error : undefined}
        onRetry={() => {
          void users.refetch()
        }}
        hasMore={users.hasNextPage}
        loadingMore={users.isFetchingNextPage}
        onLoadMore={() => {
          void users.fetchNextPage()
        }}
        empty={
          <NoUsers
            searching={
              email !== '' ||
              emailVerified !== undefined ||
              mfa !== undefined ||
              anonymous !== undefined
            }
          />
        }
      />
    </div>
  )
}
