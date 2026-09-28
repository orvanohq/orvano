import { ChevronRight, Inbox } from 'lucide-react'
import { useForm } from '@tanstack/react-form'
import { useState, type ReactNode } from 'react'
import { z } from 'zod'

import { Badge } from '@/components/ui/badge'
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from '@/components/ui/breadcrumb'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { CodeBlock } from '@/components/ui/code-block'
import {
  Combobox,
  ComboboxContent,
  ComboboxEmpty,
  ComboboxInput,
  ComboboxItem,
  ComboboxList,
} from '@/components/ui/combobox'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { CopyButton } from '@/components/ui/copy-button'
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
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuSub,
  DropdownMenuSubContent,
  DropdownMenuSubTrigger,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'
import { FormAlert } from '@/components/ui/form-alert'
import { Input } from '@/components/ui/input'
import { Kbd } from '@/components/ui/kbd'
import {
  Popover,
  PopoverContent,
  PopoverDescription,
  PopoverTitle,
  PopoverTrigger,
} from '@/components/ui/popover'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Separator } from '@/components/ui/separator'
import { Sidebar, SidebarProvider, SidebarToggle } from '@/components/ui/sidebar'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { Switch } from '@/components/ui/switch'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Textarea } from '@/components/ui/textarea'
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@/components/ui/tooltip'
import { notifyError, notifySuccess } from '@/lib/toast'
import { CreateKeyDialog } from '@/routes/_app/projects/$projectId/-keys/create-key-dialog'
import { ScopeGrid } from '@/routes/_app/projects/$projectId/-keys/scope-grid'
import { PlatformDialog } from '@/routes/_app/projects/$projectId/-platforms/platform-dialog'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import type { ApiKeyScope, Platform } from '@orvano/console-client'

/*
 * One example per inventory component (spec 0005, Component inventory). The catalog renders them
 * in every variant and state; `keyboard-scripts.ts` names them by id, and the shared browser test
 * walks each script. A new component or variant needs an example and a script entry.
 */

function ButtonExample() {
  const [count, setCount] = useState(0)
  return (
    <div className="flex flex-wrap items-center gap-2">
      <Button
        data-testid="button"
        onClick={() => {
          setCount((n) => n + 1)
        }}
      >
        Save
      </Button>
      <output data-testid="count" aria-label="Clicks">
        {count}
      </output>
      <Button variant="secondary">Secondary</Button>
      <Button variant="outline">Outline</Button>
      <Button variant="ghost">Ghost</Button>
      <Button variant="destructive">Delete</Button>
      <Button variant="link">Link</Button>
      <Button size="sm">Small</Button>
      <Button size="icon" aria-label="Add">
        <ChevronRight aria-hidden />
      </Button>
      <Button loading data-testid="button-loading">
        Loading
      </Button>
      <Button disabled>Disabled</Button>
      <Button data-testid="button-reason" disabledReason="Owners only">
        Delete project
      </Button>
    </div>
  )
}

function InputExample() {
  return (
    <div className="flex max-w-sm flex-col gap-2">
      <Input aria-label="Name" data-testid="input" placeholder="Default" />
      <Input aria-label="Invalid" aria-invalid defaultValue="Invalid" />
      <Input aria-label="Disabled" disabled defaultValue="Disabled" />
      <Input aria-label="Read only" readOnly defaultValue="Read only" />
      <Textarea aria-label="Notes" data-testid="textarea" placeholder="Textarea" />
    </div>
  )
}

function SelectExample() {
  return (
    <Select defaultValue={null}>
      <SelectTrigger data-testid="select" aria-label="Region" className="w-48">
        <SelectValue placeholder="Choose a region" />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="eu">Europe</SelectItem>
        <SelectItem value="us">United States</SelectItem>
        <SelectItem value="ap">Asia Pacific</SelectItem>
      </SelectContent>
    </Select>
  )
}

function CheckboxSwitchExample() {
  return (
    <div className="flex flex-col gap-3">
      <label className="flex items-center gap-2">
        <Checkbox data-testid="checkbox" /> Accept
      </label>
      <label className="flex items-center gap-2">
        <Checkbox indeterminate /> Some
      </label>
      <label className="flex items-center gap-2">
        <Checkbox disabled /> Disabled
      </label>
      <label className="flex items-center gap-2">
        <Switch data-testid="switch" /> Notifications
      </label>
    </div>
  )
}

function FieldExample() {
  return (
    <div className="flex max-w-sm flex-col gap-4">
      <Field>
        <FieldLabel htmlFor="field-email">Email</FieldLabel>
        <Input id="field-email" data-testid="field-input" aria-describedby="field-email-hint" />
        <FieldDescription id="field-email-hint">We never share it.</FieldDescription>
      </Field>
      <Field data-invalid>
        <FieldLabel htmlFor="field-bad">Name</FieldLabel>
        <Input id="field-bad" aria-invalid aria-describedby="field-bad-error" />
        <FieldError id="field-bad-error">Name is required</FieldError>
      </Field>
    </div>
  )
}

const nameSchema = z.object({
  name: z.string().min(1, 'Name is required').max(100, 'Name is at most 100 characters'),
})

/** The form pattern: TanStack Form, a Zod schema, Field errors under the field, a server error in the alert. */
function FormExample() {
  const [serverError, setServerError] = useState<string | null>(null)
  const form = useForm({
    defaultValues: { name: '' },
    validators: { onSubmit: nameSchema },
    onSubmit: ({ value }) => {
      if (value.name === 'taken') {
        setServerError('That name is taken.')
        return
      }
      setServerError(null)
      notifySuccess('Saved')
    },
  })
  return (
    <form
      data-testid="form"
      noValidate
      className="flex max-w-sm flex-col gap-(--stack)"
      onSubmit={(event) => {
        event.preventDefault()
        void form.handleSubmit()
      }}
    >
      {serverError === null ? null : <FormAlert title="Couldn't save">{serverError}</FormAlert>}
      <form.Field name="name">
        {(field) => {
          const invalid = field.state.meta.errors.length > 0
          return (
            <Field data-invalid={invalid || undefined}>
              <FieldLabel htmlFor="form-name">Project name</FieldLabel>
              <Input
                id="form-name"
                data-testid="form-input"
                value={field.state.value}
                aria-invalid={invalid || undefined}
                aria-describedby={invalid ? 'form-name-error' : undefined}
                onBlur={field.handleBlur}
                onChange={(event) => {
                  field.handleChange(event.target.value)
                }}
              />
              {invalid ? (
                <FieldError id="form-name-error" errors={field.state.meta.errors} />
              ) : null}
            </Field>
          )
        }}
      </form.Field>
      <Button type="submit" className="w-fit">
        Save
      </Button>
    </form>
  )
}

function FormAlertExample() {
  return (
    <div className="flex max-w-lg flex-col gap-2">
      <FormAlert variant="error" title="Couldn't save">
        The server said no.
      </FormAlert>
      <FormAlert variant="warning" title="Careful">
        This changes every key.
      </FormAlert>
      <FormAlert variant="info" title="Heads up">
        Changes apply at once.
      </FormAlert>
      <FormAlert variant="success" title="Saved" />
    </div>
  )
}

function DialogExample() {
  return (
    <Dialog>
      <DialogTrigger render={<Button data-testid="dialog-trigger" variant="outline" />}>
        Open dialog
      </DialogTrigger>
      <DialogContent data-testid="dialog">
        <DialogHeader>
          <DialogTitle>Rename project</DialogTitle>
          <DialogDescription>Give the project a new name.</DialogDescription>
        </DialogHeader>
        <Input aria-label="Project name" data-testid="dialog-input" defaultValue="Scenarios" />
        <DialogFooter>
          <DialogClose render={<Button variant="outline" />}>Cancel</DialogClose>
          <Button>Save</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function ConfirmExample() {
  return (
    <div className="flex gap-2">
      <ConfirmDialog
        trigger={
          <Button data-testid="confirm-trigger" variant="outline">
            Delete key
          </Button>
        }
        title="Delete key?"
        description="Apps using it stop working."
        confirmLabel="Delete key"
        onConfirm={() => undefined}
      />
      <ConfirmDialog
        trigger={
          <Button data-testid="confirm-typed-trigger" variant="destructive">
            Delete project
          </Button>
        }
        title="Delete project?"
        description="This removes its data after the grace period."
        confirmLabel="Delete project"
        destructive
        requireName="Scenarios"
        onConfirm={() => undefined}
      />
    </div>
  )
}

function DropdownExample() {
  const [checked, setChecked] = useState(true)
  const [radio, setRadio] = useState('a')
  return (
    <DropdownMenu>
      <DropdownMenuTrigger render={<Button data-testid="menu-trigger" variant="outline" />}>
        Actions
      </DropdownMenuTrigger>
      <DropdownMenuContent data-testid="menu">
        <DropdownMenuItem>Rename</DropdownMenuItem>
        <DropdownMenuItem>Duplicate</DropdownMenuItem>
        <DropdownMenuCheckboxItem checked={checked} onCheckedChange={setChecked}>
          Pinned
        </DropdownMenuCheckboxItem>
        <DropdownMenuRadioGroup value={radio} onValueChange={setRadio}>
          <DropdownMenuRadioItem value="a">Option A</DropdownMenuRadioItem>
          <DropdownMenuRadioItem value="b">Option B</DropdownMenuRadioItem>
        </DropdownMenuRadioGroup>
        <DropdownMenuSeparator />
        <DropdownMenuSub>
          <DropdownMenuSubTrigger>More</DropdownMenuSubTrigger>
          <DropdownMenuSubContent>
            <DropdownMenuItem>Export</DropdownMenuItem>
          </DropdownMenuSubContent>
        </DropdownMenuSub>
        <DropdownMenuItem variant="destructive">Delete</DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

function PopoverExample() {
  return (
    <Popover>
      <PopoverTrigger render={<Button data-testid="popover-trigger" variant="outline" />}>
        Details
      </PopoverTrigger>
      <PopoverContent data-testid="popover">
        <PopoverTitle>Project ID</PopoverTitle>
        <PopoverDescription>Send it as X-Orvano-Project.</PopoverDescription>
      </PopoverContent>
    </Popover>
  )
}

const fruit = ['Apple', 'Apricot', 'Banana', 'Cherry']

function ComboboxExample() {
  return (
    <Combobox items={fruit}>
      <ComboboxInput data-testid="combobox" aria-label="Fruit" placeholder="Search fruit" />
      <ComboboxContent data-testid="combobox-list">
        <ComboboxEmpty>No fruit found</ComboboxEmpty>
        <ComboboxList aria-label="Fruit">
          {(item: string) => (
            <ComboboxItem key={item} value={item}>
              {item}
            </ComboboxItem>
          )}
        </ComboboxList>
      </ComboboxContent>
    </Combobox>
  )
}

function TooltipExample() {
  return (
    <TooltipProvider>
      <Tooltip>
        <TooltipTrigger render={<Button data-testid="tooltip-trigger" variant="outline" />}>
          Hover or focus
        </TooltipTrigger>
        <TooltipContent data-testid="tooltip">Copies the ID</TooltipContent>
      </Tooltip>
    </TooltipProvider>
  )
}

function TabsExample() {
  return (
    <Tabs defaultValue="one">
      <TabsList aria-label="Sections">
        <TabsTrigger value="one" data-testid="tab-one">
          One
        </TabsTrigger>
        <TabsTrigger value="two" data-testid="tab-two">
          Two
        </TabsTrigger>
      </TabsList>
      <TabsContent value="one">First panel</TabsContent>
      <TabsContent value="two">Second panel</TabsContent>
    </Tabs>
  )
}

function ToastExample() {
  return (
    <div className="flex gap-2">
      <Button
        data-testid="toast-success"
        variant="outline"
        onClick={() => {
          notifySuccess('Saved', 'Your changes are live.')
        }}
      >
        Success toast
      </Button>
      <Button
        data-testid="toast-error"
        variant="outline"
        onClick={() => {
          notifyError('Could not save')
        }}
      >
        Error toast
      </Button>
    </div>
  )
}

interface Row {
  name: string
  region: string
}

const rows: Row[] = [
  { name: 'Beta', region: 'eu' },
  { name: 'Alpha', region: 'us' },
]

const columns: ColumnDef<Row>[] = [
  { accessorKey: 'name', header: 'Name' },
  { accessorKey: 'region', header: 'Region' },
]

function TableExample() {
  return (
    <div className="flex max-w-lg flex-col gap-4">
      <div data-testid="table">
        <DataTable
          label="Apps"
          columns={columns}
          data={rows}
          sortable
          hasMore
          onLoadMore={() => undefined}
        />
      </div>
      <DataTable label="Loading apps" columns={columns} data={[]} loading />
      <DataTable
        label="No apps"
        columns={columns}
        data={[]}
        empty={
          <Empty className="border">
            <EmptyHeader>
              <EmptyTitle>No apps</EmptyTitle>
            </EmptyHeader>
          </Empty>
        }
      />
      <DataTable
        label="Broken apps"
        columns={columns}
        data={[]}
        error={new Error('The API is down')}
      />
    </div>
  )
}

function BadgeExample() {
  return (
    <div className="flex flex-wrap gap-2">
      <Badge>Neutral</Badge>
      <Badge variant="primary">Primary</Badge>
      <Badge variant="success">Success</Badge>
      <Badge variant="warning">Warning</Badge>
      <Badge variant="destructive">Destructive</Badge>
      <Badge variant="status" tone="success">
        Active
      </Badge>
      <Badge variant="status" tone="warning">
        Setting up
      </Badge>
      <Badge variant="status" tone="danger">
        Failed
      </Badge>
      <Badge variant="status" tone="neutral">
        Deleting
      </Badge>
    </div>
  )
}

function CardExample() {
  return (
    <div className="grid max-w-xl gap-3 sm:grid-cols-2">
      <Card>
        <CardHeader>
          <CardTitle>Default card</CardTitle>
          <CardDescription>A panel of related content.</CardDescription>
        </CardHeader>
        <CardContent>Content</CardContent>
      </Card>
      <a
        href="#interactive"
        data-testid="card-link"
        className="block rounded-lg border border-border bg-card p-3 hover:bg-accent"
      >
        <span className="font-medium">Interactive card</span>
        <span className="block text-muted-foreground">The whole card is one link.</span>
      </a>
    </div>
  )
}

function EmptyExample() {
  return (
    <Empty className="border">
      <EmptyHeader>
        <EmptyMedia variant="icon">
          <Inbox aria-hidden />
        </EmptyMedia>
        <EmptyTitle>Nothing here yet</EmptyTitle>
        <EmptyDescription>Things you add appear here.</EmptyDescription>
      </EmptyHeader>
      <Button variant="outline">Add something</Button>
    </Empty>
  )
}

function LoadingExample() {
  return (
    <div aria-busy className="flex items-center gap-4">
      <Skeleton aria-hidden className="h-4 w-40" />
      <Skeleton aria-hidden className="size-8 rounded-full" />
      <Skeleton aria-hidden className="h-16 w-32" />
      <Spinner aria-hidden />
    </div>
  )
}

function BreadcrumbExample() {
  return (
    <Breadcrumb>
      <BreadcrumbList>
        <BreadcrumbItem>
          <BreadcrumbLink href="#orgs" data-testid="crumb">
            Orgs
          </BreadcrumbLink>
        </BreadcrumbItem>
        <BreadcrumbSeparator />
        <BreadcrumbItem>
          <BreadcrumbLink href="#fixtures">Fixtures</BreadcrumbLink>
        </BreadcrumbItem>
        <BreadcrumbSeparator />
        <BreadcrumbItem>
          <BreadcrumbPage>Scenarios</BreadcrumbPage>
        </BreadcrumbItem>
      </BreadcrumbList>
    </Breadcrumb>
  )
}

function SidebarExample() {
  return (
    <SidebarProvider>
      <div className="flex h-48 border border-border">
        <Sidebar label="Example">
          <nav aria-label="Example navigation" className="flex-1 p-2">
            <a href="#one" className="block rounded-md px-2 py-1 hover:bg-sidebar-accent">
              One
            </a>
          </nav>
          <SidebarToggle />
        </Sidebar>
      </div>
    </SidebarProvider>
  )
}

function SeparatorKbdExample() {
  return (
    <div className="flex items-center gap-3">
      <span>Toggle the sidebar</span>
      <Kbd>[</Kbd>
      <Separator orientation="vertical" className="h-5" />
      <Kbd>Esc</Kbd>
    </div>
  )
}

function CopyExample() {
  return (
    <div className="flex items-center gap-1">
      <code className="font-mono">scenarios0000000000a</code>
      <span data-testid="copy">
        <CopyButton value="scenarios0000000000a" label="Copy project ID" />
      </span>
    </div>
  )
}

function CodeExample() {
  return (
    <div className="max-w-lg">
      <CodeBlock label="install command" code="pnpm add @orvano/js" />
    </div>
  )
}

/** A stand in secret for the catalog; it authenticates nothing (spec 0007, key invariants). */
const exampleSecret = 'orv_example_not_a_real_key_000000000000000000'

// The reveal step (spec 0007, AC-14): the real dialog, with a stand in for the create call.
function KeyRevealExample() {
  const [queryClient] = useState(() => new QueryClient())
  const [open, setOpen] = useState(false)
  return (
    <QueryClientProvider client={queryClient}>
      <Button
        data-testid="key-create"
        onClick={() => {
          setOpen(true)
        }}
      >
        Create key
      </Button>
      <CreateKeyDialog
        projectId="scenarios0000000000a"
        open={open}
        onOpenChange={setOpen}
        createKey={(body) =>
          Promise.resolve({
            secret: exampleSecret,
            apiKey: {
              id: 'key00000000000000000a',
              name: body.name,
              prefix: exampleSecret.slice(0, 12),
              scopes: body.scopes,
              expiresAt: body.expiresAt ?? null,
              lastUsedAt: null,
              createdByUserId: 'user0000000000000000a',
              createdAt: '2026-06-01T10:00:00.000Z',
            },
          })
        }
      />
    </QueryClientProvider>
  )
}

function ScopeGridExample() {
  const [value, setValue] = useState<ApiKeyScope[]>([])
  return (
    <div className="max-w-lg" data-testid="scope-grid">
      <ScopeGrid
        id="example-scope"
        value={value}
        onChange={setValue}
        invalid={false}
        describedBy={undefined}
      />
    </div>
  )
}

const examplePlatform: Platform = {
  id: 'platform000000000000a',
  type: 'android',
  name: 'Android app',
  identifier: 'com.example.app',
  createdAt: '2026-06-01T10:00:00.000Z',
  updatedAt: '2026-06-01T10:00:00.000Z',
}

// The platform form (spec 0007, AC-18 to AC-20), adding and editing, with nothing sent anywhere.
function PlatformFormExample() {
  const [adding, setAdding] = useState(false)
  const [editing, setEditing] = useState(false)
  return (
    <div className="flex gap-2">
      <Button
        data-testid="platform-add"
        onClick={() => {
          setAdding(true)
        }}
      >
        Add platform
      </Button>
      <Button
        data-testid="platform-edit"
        variant="outline"
        onClick={() => {
          setEditing(true)
        }}
      >
        Edit platform
      </Button>
      <PlatformDialog open={adding} onOpenChange={setAdding} onSubmit={() => Promise.resolve()} />
      <PlatformDialog
        platform={examplePlatform}
        open={editing}
        onOpenChange={setEditing}
        onSubmit={() => Promise.resolve()}
      />
    </div>
  )
}

/** The catalog: id to a titled, rendered example. */
export const examples: Record<string, { title: string; render: () => ReactNode }> = {
  button: { title: 'Button', render: () => <ButtonExample /> },
  input: { title: 'Input and Textarea', render: () => <InputExample /> },
  select: { title: 'Select', render: () => <SelectExample /> },
  'checkbox-switch': { title: 'Checkbox and Switch', render: () => <CheckboxSwitchExample /> },
  field: { title: 'Field', render: () => <FieldExample /> },
  form: { title: 'Form (TanStack Form and Zod)', render: () => <FormExample /> },
  'form-alert': { title: 'Form alert', render: () => <FormAlertExample /> },
  dialog: { title: 'Dialog', render: () => <DialogExample /> },
  'confirm-dialog': { title: 'Confirm dialog', render: () => <ConfirmExample /> },
  'dropdown-menu': { title: 'Dropdown menu', render: () => <DropdownExample /> },
  popover: { title: 'Popover', render: () => <PopoverExample /> },
  combobox: { title: 'Combobox', render: () => <ComboboxExample /> },
  tooltip: { title: 'Tooltip', render: () => <TooltipExample /> },
  tabs: { title: 'Tabs', render: () => <TabsExample /> },
  toast: { title: 'Toast', render: () => <ToastExample /> },
  table: { title: 'Table and DataTable', render: () => <TableExample /> },
  badge: { title: 'Badge', render: () => <BadgeExample /> },
  card: { title: 'Card', render: () => <CardExample /> },
  empty: { title: 'Empty state', render: () => <EmptyExample /> },
  loading: { title: 'Skeleton and Spinner', render: () => <LoadingExample /> },
  breadcrumb: { title: 'Breadcrumb', render: () => <BreadcrumbExample /> },
  sidebar: { title: 'Sidebar', render: () => <SidebarExample /> },
  'separator-kbd': { title: 'Separator and Kbd', render: () => <SeparatorKbdExample /> },
  'copy-button': { title: 'Copy button', render: () => <CopyExample /> },
  'code-block': { title: 'Code block', render: () => <CodeExample /> },
  'key-reveal': { title: 'API key reveal step', render: () => <KeyRevealExample /> },
  'scope-grid': { title: 'Scope grid', render: () => <ScopeGridExample /> },
  'platform-form': { title: 'Platform form', render: () => <PlatformFormExample /> },
}
