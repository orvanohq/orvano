import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { Search } from 'lucide-react'
import { useEffect, useState } from 'react'

import { DataTable } from '@/components/ui/data-table'
import { InputGroup, InputGroupAddon, InputGroupInput } from '@/components/ui/input-group'
import { Label } from '@/components/ui/label'
import { projectClient } from '@/lib/console-client'
import { usePageTitle } from '@/lib/page-title'
import { keys, projectQuery, usersQuery } from '@/lib/queries'
import { roleReason, useOrgRole } from '@/lib/roles'
import { notifySuccess } from '@/lib/toast'
import { meetsRole } from '@/shell/nav'
import { PageHeading } from '@/shell/page-heading'

import { CreateUserDialog, NoUsers, userColumns } from '../-users/parts'

export const Route = createFileRoute('/_app/projects/$projectId/users/')({
  component: UsersPage,
})

/**
 * The project's users (spec 0004, AC-29): newest first, paged, with a prefix search on email.
 * Owners and developers create users; viewers see the button with the reason it is not theirs.
 */
function UsersPage() {
  const { projectId } = Route.useParams()
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
  const users = useInfiniteQuery(usersQuery(projectId, email))
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
      <div className="flex max-w-sm flex-col gap-1.5">
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
        empty={<NoUsers searching={email !== ''} />}
      />
    </div>
  )
}
