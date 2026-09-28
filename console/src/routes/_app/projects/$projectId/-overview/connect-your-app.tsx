import { Link } from '@tanstack/react-router'
import { CircleCheck } from 'lucide-react'

import { SettingsSection } from '@/shell/settings-section'

/**
 * The overview's "Connect your app" card (spec 0007, AC-21): add a platform, then create an API
 * key. A step shows as done with a check and the word "Done", never color alone. `undefined` means
 * that step's check is still loading, so it shows no state yet.
 */
export function ConnectYourApp({
  projectId,
  hasPlatform,
  hasKey,
}: {
  projectId: string
  hasPlatform: boolean | undefined
  hasKey: boolean | undefined
}) {
  return (
    <SettingsSection
      title="Connect your app"
      description="Two steps before an SDK can talk to this project."
    >
      <ol className="flex flex-col gap-3">
        <Step
          number={1}
          done={hasPlatform}
          to="/projects/$projectId/platforms"
          projectId={projectId}
          title="Add a platform"
          description="The web hosts and app IDs allowed to call this project."
        />
        <Step
          number={2}
          done={hasKey}
          to="/projects/$projectId/keys"
          projectId={projectId}
          title="Create an API key"
          description="For your server code, with only the scopes it needs."
        />
      </ol>
    </SettingsSection>
  )
}

function Step({
  number,
  done,
  to,
  projectId,
  title,
  description,
}: {
  number: number
  done: boolean | undefined
  to: '/projects/$projectId/platforms' | '/projects/$projectId/keys'
  projectId: string
  title: string
  description: string
}) {
  return (
    <li className="flex items-start gap-3">
      <span
        aria-hidden
        className="flex size-6 shrink-0 items-center justify-center rounded-full border border-border text-small text-muted-foreground"
      >
        {done === true ? <CircleCheck className="size-(--icon) text-success-text" /> : number}
      </span>
      <div className="flex min-w-0 flex-1 flex-col gap-0.5">
        <Link
          to={to}
          params={{ projectId }}
          className="w-fit font-medium text-link hover:underline"
        >
          {title}
        </Link>
        <span className="text-muted-foreground">{description}</span>
      </div>
      {done === true ? <span className="text-success-text">Done</span> : null}
    </li>
  )
}
