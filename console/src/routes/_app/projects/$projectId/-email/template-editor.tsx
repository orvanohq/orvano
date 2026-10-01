import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query'
import { useBlocker } from '@tanstack/react-router'
import { Suspense, lazy, useEffect, useRef, useState } from 'react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { CopyButton } from '@/components/ui/copy-button'
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'
import { FormAlert } from '@/components/ui/form-alert'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { projectClient } from '@/lib/console-client'
import { describeError } from '@/lib/errors'
import { emailTemplateQuery, keys } from '@/lib/queries'
import { useStateMoved } from '@/lib/state-moved'
import { notifySuccess } from '@/lib/toast'
import { RelativeTime } from '@/shell/relative-time'
import type { EmailTemplate, RenderedEmail } from '@orvano/console-client'

import { EmailPreviewFrame } from './email-preview-frame'
import {
  initialValues,
  isChanged,
  placeError,
  previewDelayMs,
  toInput,
  type TemplatePart,
} from './template-fields'

// CodeMirror is large and only this page needs it, so it loads with the editor (spec 0009, Stack).
const CodeEditor = lazy(() =>
  import('./code-editor').then((module) => ({ default: module.CodeEditor })),
)

type Action = 'save' | 'test'

/** What happened last: a test that arrived, or a failure that belongs to no single part. */
type Notice = { kind: 'sent'; to: string } | { kind: 'failed'; title: string; message: string }

/** `value`, once it has stopped changing for `delayMs`. */
function useDebounced<T>(value: T, delayMs: number): T {
  const [settled, setSettled] = useState(value)
  useEffect(() => {
    const timer = window.setTimeout(() => {
      setSettled(value)
    }, delayMs)
    return () => {
      window.clearTimeout(timer)
    }
  }, [value, delayMs])
  return settled
}

/**
 * The editor of one auth email template (spec 0009, AC-9 to AC-12): Subject, HTML, and an optional
 * Text part, the variables the template can use, and a preview that follows the typing. Save stays
 * off until something changed; Send test sends the content as it is, saved or not; Reset to default
 * shows for a custom template. Leaving with unsaved changes asks first.
 *
 * With `readOnlyReason` (a viewer) the editors are read only, and there is no preview, Save, Reset,
 * or Send test.
 */
export function TemplateEditor({
  projectId,
  orgId,
  template,
  name,
  description,
  readOnlyReason,
}: {
  projectId: string
  /** The project's org, so a project that moved under you refetches the right lists. */
  orgId: string | undefined
  template: EmailTemplate
  /** The template's name, for example "Password reset". */
  name: string
  /** When it is sent. */
  description: string | undefined
  readOnlyReason: string | undefined
}) {
  const kind = template.kind
  const readOnly = readOnlyReason !== undefined
  const client = projectClient(projectId)
  const queryClient = useQueryClient()
  const stateMoved = useStateMoved()
  const target = { projectId, orgId }

  // What is stored: the editor compares against it, and a save or a reset moves it.
  const [stored, setStored] = useState(template)
  const [values, setValues] = useState(() => initialValues(template))
  // Bumped when the text is replaced from outside (a reset), so the editors start over with it.
  const [generation, setGeneration] = useState(0)
  const [serverErrors, setServerErrors] = useState<Partial<Record<TemplatePart, string>>>({})
  const [notice, setNotice] = useState<Notice | null>(null)
  const [running, setRunning] = useState<Action | null>(null)
  const changed = isChanged(values, initialValues(stored))

  const settled = useDebounced(values, previewDelayMs)
  const preview = useQuery({
    queryKey: [...keys.emailTemplate(projectId, kind), 'preview', settled] as const,
    queryFn: ({ signal }) =>
      client.consoleEmailTemplates.preview(kind, toInput(settled), { signal }),
    enabled: !readOnly,
    placeholderData: keepPreviousData,
    retry: false,
    staleTime: Infinity,
    gcTime: 0,
  })
  const previewProblem = preview.isError ? placeError(preview.error) : null
  // The last successful preview stays through a 422 or a failure (AC-33), so fixing an error never
  // reloads the frame. keepPreviousData keeps it only while a request is on its way.
  const [shown, setShown] = useState<RenderedEmail | undefined>(undefined)
  if (preview.data !== undefined && preview.data !== shown) setShown(preview.data)

  const setPart = (part: TemplatePart, value: string) => {
    setValues((current) => ({ ...current, [part]: value }))
    setServerErrors((current) =>
      current[part] === undefined ? current : { ...current, [part]: undefined },
    )
  }

  /** The error under a part: what a save or test said, else what the preview says right now. */
  const errorOf = (part: TemplatePart): string | undefined =>
    serverErrors[part] ?? (previewProblem?.part === part ? previewProblem.message : undefined)

  const run = async (action: Action) => {
    setServerErrors({})
    setNotice(null)
    setRunning(action)
    try {
      if (action === 'save') {
        const saved = await client.consoleEmailTemplates.update(kind, toInput(values))
        queryClient.setQueryData(keys.emailTemplate(projectId, kind), saved)
        void queryClient.invalidateQueries({
          queryKey: keys.emailTemplates(projectId),
          predicate: (query) => query.queryKey.at(-1) === 'catalog',
        })
        setStored(saved)
        // The server trims the subject and drops a blank text part; show what it kept.
        setValues((current) => ({ ...current, subject: saved.subject }))
        notifySuccess('Template saved', `${name} emails use your version now.`)
      } else {
        const result = await client.consoleEmailTemplates.test(kind, toInput(values))
        setNotice({ kind: 'sent', to: result.sentTo })
      }
    } catch (error) {
      const title =
        action === 'save' ? "Couldn't save the template" : "Couldn't send the test email"
      const placed = placeError(error)
      if (placed !== null) {
        setServerErrors({ [placed.part]: placed.message })
      } else if (!stateMoved(title, error, target)) {
        setNotice({ kind: 'failed', title, message: describeError(error).message })
      }
    } finally {
      setRunning(null)
    }
  }

  const reset = async () => {
    try {
      await client.consoleEmailTemplates.reset(kind)
    } catch (error) {
      stateMoved("Couldn't reset the template", error, target)
      throw error
    }
    await queryClient.invalidateQueries({ queryKey: keys.emailTemplates(projectId) })
    const fresh = await queryClient.query({ ...emailTemplateQuery(projectId, kind), staleTime: 0 })
    setStored(fresh)
    setValues(initialValues(fresh))
    setServerErrors({})
    setNotice(null)
    setGeneration((current) => current + 1)
    notifySuccess('Template reset', `${name} emails use the default again.`)
  }

  // Read through a ref, so the blocker always sees the latest answer.
  const unsaved = useRef(false)
  useEffect(() => {
    unsaved.current = changed && !readOnly
  }, [changed, readOnly])
  const blocker = useBlocker({
    shouldBlockFn: () => unsaved.current,
    enableBeforeUnload: () => unsaved.current,
    withResolver: true,
  })

  const editor = (part: 'html' | 'text', label: string, height: string, placeholder?: string) => {
    const error = errorOf(part)
    return (
      <Field data-invalid={error !== undefined || undefined}>
        <span className="text-sm font-medium">{label}</span>
        <Suspense fallback={<Skeleton aria-hidden className={height} />}>
          <CodeEditor
            key={generation}
            label={label}
            language={part}
            value={values[part]}
            placeholder={placeholder}
            readOnly={readOnly}
            invalid={error !== undefined}
            className={height}
            onChange={(next) => {
              setPart(part, next)
            }}
          />
        </Suspense>
        {part === 'html' ? (
          <FieldDescription>
            Tab indents. Press Escape, then Tab, to leave an editor.
          </FieldDescription>
        ) : null}
        {error === undefined ? null : <FieldError errors={[{ message: error }]} />}
      </Field>
    )
  }

  const subjectError = errorOf('subject')

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <div className="flex flex-wrap items-center gap-3">
          <h2 className="text-lg font-semibold">{name}</h2>
          <Badge variant={stored.isCustom ? 'primary' : 'neutral'}>
            {stored.isCustom ? 'Custom' : 'Default'}
          </Badge>
          {stored.updatedAt === null ? null : (
            <span className="text-small text-muted-foreground">
              Edited <RelativeTime iso={stored.updatedAt} />
            </span>
          )}
        </div>
        {description === undefined ? null : (
          <p className="max-w-prose text-muted-foreground">{description}</p>
        )}
      </div>

      {readOnly ? (
        <FormAlert variant="info" title="You can read this template">
          Editing, previews, and test emails are for developers and owners.
        </FormAlert>
      ) : null}

      <div className="grid items-start gap-6 lg:grid-cols-2">
        <form
          noValidate
          aria-label={`${name} template`}
          className="flex min-w-0 flex-col gap-(--stack)"
          onSubmit={(event) => {
            event.preventDefault()
            if (!readOnly && changed && running === null) void run('save')
          }}
        >
          <Field data-invalid={subjectError !== undefined || undefined}>
            <FieldLabel htmlFor="template-subject">Subject</FieldLabel>
            <Input
              id="template-subject"
              value={values.subject}
              readOnly={readOnly}
              autoComplete="off"
              spellCheck={false}
              maxLength={255}
              aria-invalid={subjectError !== undefined || undefined}
              aria-describedby={subjectError === undefined ? undefined : 'template-subject-error'}
              onChange={(event) => {
                setPart('subject', event.target.value)
              }}
            />
            {subjectError === undefined ? null : (
              <FieldError id="template-subject-error" errors={[{ message: subjectError }]} />
            )}
          </Field>
          {editor('html', 'HTML', 'h-96')}
          {editor('text', 'Text', 'h-48', 'Leave empty to generate it from the HTML')}

          {readOnly ? null : (
            <>
              {notice === null ? null : notice.kind === 'sent' ? (
                <FormAlert variant="success" title="Test email sent">
                  Sent to {notice.to}. Check your inbox.
                </FormAlert>
              ) : (
                <FormAlert title={notice.title}>{notice.message}</FormAlert>
              )}
              <div className="flex flex-wrap items-center gap-2">
                <Button
                  type="submit"
                  loading={running === 'save'}
                  disabled={running === 'test'}
                  disabledReason={changed ? undefined : 'Nothing to save yet'}
                >
                  Save
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  loading={running === 'test'}
                  disabled={running === 'save'}
                  onClick={() => {
                    void run('test')
                  }}
                >
                  Send test
                </Button>
                {stored.isCustom ? (
                  <div className="ml-auto">
                    <ConfirmDialog
                      trigger={<Button variant="outline">Reset to default</Button>}
                      title="Reset to the default template?"
                      description="Your version is deleted, along with any changes you haven’t saved, and these emails use the default again."
                      confirmLabel="Reset to default"
                      destructive
                      onConfirm={reset}
                    />
                  </div>
                ) : null}
              </div>
            </>
          )}
        </form>

        <div className="flex min-w-0 flex-col gap-6">
          {readOnly ? null : (
            <TemplatePreview
              rendered={shown}
              stale={preview.isFetching || settled !== values}
              // A problem in one part already shows under that part.
              failure={preview.isError && previewProblem === null ? preview.error : null}
              blocked={previewProblem !== null}
            />
          )}
          <section aria-labelledby="template-variables" className="flex flex-col gap-3">
            <div className="flex flex-col gap-1">
              <h3 id="template-variables" className="text-h3 font-semibold">
                Variables
              </h3>
              <p className="text-muted-foreground">
                What this template can use. Previews and test emails fill them with the samples.
              </p>
            </div>
            <ul className="divide-y divide-border rounded-lg border border-border bg-card">
              {template.variables.map((variable) => (
                <li key={variable.name} className="flex items-start gap-2 px-3 py-2">
                  <div className="flex min-w-0 flex-1 flex-col gap-0.5">
                    <code className="font-mono text-mono">{`{{ ${variable.name} }}`}</code>
                    <span className="text-muted-foreground">{variable.description}</span>
                    <span className="text-small break-all text-muted-foreground">
                      Sample: {variable.sample === '' ? 'empty' : variable.sample}
                    </span>
                  </div>
                  <CopyButton value={`{{ ${variable.name} }}`} label={`Copy ${variable.name}`} />
                </li>
              ))}
            </ul>
          </section>
        </div>
      </div>

      <ConfirmDialog
        open={blocker.status === 'blocked'}
        onOpenChange={(open) => {
          if (!open) blocker.reset?.()
        }}
        title="Leave without saving?"
        description="Your changes to this template aren’t saved yet. If you leave, they are lost."
        confirmLabel="Leave without saving"
        destructive
        onConfirm={() => {
          blocker.proceed?.()
        }}
      />
    </div>
  )
}

/**
 * The preview (spec 0009, AC-9, AC-33): the rendered subject, then the HTML in the preview frame,
 * or the text part. While a newer render is on its way, or the template has an error, the last
 * successful one stays, dimmed. The HTML view stays mounted while the Text view shows, so switching
 * never reloads the frame.
 */
function TemplatePreview({
  rendered,
  stale,
  failure,
  blocked,
}: {
  rendered: RenderedEmail | undefined
  /** True while what shows is older than what is typed. */
  stale: boolean
  /** A failure that belongs to no part of the template, for example a rate limit. */
  failure: unknown
  /** True when a part has an error, so there is nothing new to show. */
  blocked: boolean
}) {
  return (
    <section aria-labelledby="template-preview" aria-busy={stale} className="flex flex-col gap-3">
      <div className="flex flex-col gap-1">
        <h3 id="template-preview" className="text-h3 font-semibold">
          Preview
        </h3>
        <p className="text-muted-foreground">How the email looks with the sample values.</p>
      </div>
      {failure === null ? null : (
        <FormAlert title="Couldn't load the preview">{describeError(failure).message}</FormAlert>
      )}
      {blocked ? (
        <FormAlert variant="warning" title="The preview is waiting">
          Fix the problem shown under the template, and the preview comes back.
        </FormAlert>
      ) : null}
      {rendered === undefined ? (
        failure === null && !blocked ? (
          <Skeleton aria-hidden className="h-96 w-full" />
        ) : null
      ) : (
        <div
          className={
            stale || blocked || failure !== null
              ? 'flex flex-col gap-3 opacity-60'
              : 'flex flex-col gap-3'
          }
        >
          <dl className="rounded-lg border border-border bg-card px-3 py-2">
            <dt className="text-small text-muted-foreground">Subject</dt>
            <dd data-testid="preview-subject" className="font-medium break-words">
              {rendered.subject}
            </dd>
          </dl>
          <Tabs defaultValue="html">
            <TabsList aria-label="Preview part">
              <TabsTrigger value="html">HTML</TabsTrigger>
              <TabsTrigger value="text">Text</TabsTrigger>
            </TabsList>
            <TabsContent value="html" keepMounted>
              {/* Emails assume a white page; the frame page is white whatever the console's theme. */}
              <EmailPreviewFrame
                html={rendered.html}
                className="h-128 w-full rounded-lg border border-border bg-white"
              />
            </TabsContent>
            <TabsContent value="text">
              <pre
                tabIndex={0}
                aria-label="Text part preview"
                className="h-128 overflow-auto rounded-lg border border-border bg-card p-3 font-mono text-mono whitespace-pre-wrap"
              >
                {rendered.text}
              </pre>
            </TabsContent>
          </Tabs>
        </div>
      )}
    </section>
  )
}
