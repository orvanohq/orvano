import { PageHeading } from '@/shell/page-heading'
import { usePreferences, type Density, type ThemeChoice } from '@/lib/preferences'
import { examples } from '@/dev/examples'

// The marker below is how the production build is checked: no built file may contain it (AC-9).
const marker = 'orvano-dev-catalog'

const themes: ThemeChoice[] = ['dark', 'light', 'system']
const densities: Density[] = ['compact', 'comfortable']

/**
 * Every inventory component in every variant and state, with theme and density switches. Dev only:
 * `routes/dev.components.tsx` imports it lazily inside an `import.meta.env.DEV` branch so the
 * build drops it (spec 0005, AC-9).
 */
export function Catalog() {
  const { theme, density, setTheme, setDensity } = usePreferences()
  return (
    <main id="main" data-marker={marker} className="mx-auto flex max-w-5xl flex-col gap-10 px-(--page-px) py-8">
      <div className="flex flex-col gap-4">
        <PageHeading>Components</PageHeading>
        <p className="text-muted-foreground">
          Every component in every variant and state. Hover, focus, and press to see the rest.
        </p>
        <div className="flex flex-wrap gap-6">
          <Switch label="Theme" options={themes} value={theme} onChange={setTheme} />
          <Switch label="Density" options={densities} value={density} onChange={setDensity} />
        </div>
        <nav aria-label="Components" className="flex flex-wrap gap-x-4 gap-y-1">
          {Object.entries(examples).map(([id, { title }]) => (
            <a key={id} href={`#example-${id}`} className="text-link hover:underline">
              {title}
            </a>
          ))}
        </nav>
      </div>
      {Object.entries(examples).map(([id, { title, render }]) => (
        <section key={id} id={`example-${id}`} aria-labelledby={`title-${id}`} className="flex flex-col gap-3">
          <h2 id={`title-${id}`} className="border-b border-border pb-2 text-lg/7 font-semibold">
            {title}
          </h2>
          {render()}
        </section>
      ))}
    </main>
  )
}

function Switch<T extends string>({
  label,
  options,
  value,
  onChange,
}: {
  label: string
  options: T[]
  value: T
  onChange: (value: T) => void
}) {
  return (
    <fieldset className="flex items-center gap-3">
      <legend className="sr-only">{label}</legend>
      <span className="text-muted-foreground" aria-hidden>
        {label}
      </span>
      {options.map((option) => (
        <label key={option} className="flex items-center gap-1 capitalize">
          <input
            type="radio"
            name={label}
            checked={value === option}
            onChange={() => {
              onChange(option)
            }}
          />
          {option}
        </label>
      ))}
    </fieldset>
  )
}
