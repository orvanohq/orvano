import { Checkbox } from '@/components/ui/checkbox'
import { scopeRows, scopes } from '@/lib/scopes'
import type { ApiKeyScope } from '@orvano/console-client'

/**
 * The Create key form's scopes (spec 0007, AC-13): one row per resource with a Read and a Write
 * column, each checkbox beside its description. A resource without one of the two shows an empty
 * cell there. Each checkbox is named "<Resource> read" or "<Resource> write".
 */
export function ScopeGrid({
  id,
  value,
  onChange,
  invalid,
  describedBy,
}: {
  /** Prefix for element IDs, unique on the page. */
  id: string
  value: readonly ApiKeyScope[]
  onChange: (next: ApiKeyScope[]) => void
  invalid: boolean
  /** The ID of the group's error, when it has one. */
  describedBy: string | undefined
}) {
  const toggle = (scope: ApiKeyScope, on: boolean) => {
    const rest = value.filter((item) => item !== scope)
    onChange(on ? [...rest, scope] : rest)
  }
  const cell = (resource: string, scope: ApiKeyScope | undefined) => {
    if (scope === undefined) return <td className="p-2" />
    const descriptionId = `${id}-${scope}`
    return (
      <td className="p-2 align-top">
        <label className="flex items-start gap-2">
          <Checkbox
            className="mt-0.5"
            aria-label={`${resource} ${scopes[scope].access}`}
            aria-describedby={descriptionId}
            aria-invalid={invalid || undefined}
            checked={value.includes(scope)}
            onCheckedChange={(checked) => {
              toggle(scope, checked)
            }}
          />
          <span id={descriptionId} className="text-small text-muted-foreground">
            {scopes[scope].description}
          </span>
        </label>
      </td>
    )
  }

  return (
    <fieldset aria-describedby={describedBy} className="flex flex-col gap-1.5">
      <legend className="mb-1.5 text-sm font-medium">Scopes</legend>
      <div className="overflow-x-auto rounded-lg border border-border">
        <table className="w-full text-sm">
          <thead className="bg-muted/50 text-left text-small text-muted-foreground">
            <tr>
              <th scope="col" className="p-2 font-medium">
                Resource
              </th>
              <th scope="col" className="p-2 font-medium">
                Read
              </th>
              <th scope="col" className="p-2 font-medium">
                Write
              </th>
            </tr>
          </thead>
          <tbody>
            {scopeRows().map((row) => (
              <tr key={row.resource} className="border-t border-border">
                <th scope="row" className="p-2 text-left align-top font-medium">
                  {row.resource}
                </th>
                {cell(row.resource, row.read)}
                {cell(row.resource, row.write)}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </fieldset>
  )
}
