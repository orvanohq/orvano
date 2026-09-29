import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { orgRoles, roleInfo } from '@/lib/roles'
import type { OrgRole } from '@orvano/console-client'

/**
 * Owner, Developer, and Viewer, each with its one line description (spec 0008, AC-16 and AC-19).
 * Labelled by the element whose id is `labelledBy`; arrow keys move and select.
 */
export function RoleRadioGroup({
  id,
  value,
  onChange,
  labelledBy,
}: {
  id: string
  value: OrgRole
  onChange: (role: OrgRole) => void
  labelledBy: string
}) {
  return (
    <RadioGroup
      aria-labelledby={labelledBy}
      value={value}
      onValueChange={(next) => {
        onChange(next as OrgRole)
      }}
    >
      {orgRoles.map((role) => {
        const itemId = `${id}-${role}`
        return (
          <label
            key={role}
            htmlFor={itemId}
            className="flex cursor-pointer items-start gap-3 rounded-md border border-input p-3 has-data-checked:border-primary has-data-checked:bg-accent"
          >
            <RadioGroupItem
              id={itemId}
              value={role}
              aria-labelledby={`${itemId}-label`}
              aria-describedby={`${itemId}-description`}
              className="mt-0.5"
            />
            <span className="flex flex-col gap-0.5">
              <span id={`${itemId}-label`} className="font-medium">
                {roleInfo[role].label}
              </span>
              <span id={`${itemId}-description`} className="text-small text-muted-foreground">
                {roleInfo[role].description}
              </span>
            </span>
          </label>
        )
      })}
    </RadioGroup>
  )
}
