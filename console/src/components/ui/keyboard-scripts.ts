/**
 * The Keyboard column of spec 0005's Component inventory as data. The shared browser test
 * (`components.browser.test.tsx`) renders each script's catalog example, presses the keys in order,
 * checks what must hold after each step, and runs axe at the end in both themes. A new component
 * or variant is not done until it has an entry here (and an example in `src/dev/examples.tsx`).
 */

/** Something that must hold after a key press. Selectors are CSS selectors for the whole page. */
export type Check =
  /** The element matching the selector has focus. */
  | { focused: string }
  /** An attribute has this value; `null` means the attribute is absent. */
  | { attribute: { selector: string; name: string; value: string | null } }
  /** An element matching the selector exists (and, for `visible`, is shown). */
  | { present: string }
  | { absent: string }
  /** The element's text contains this string. */
  | { text: { selector: string; contains: string } }
  /** The input's value. */
  | { value: { selector: string; is: string } }

/**
 * One key press: `keys` uses `userEvent.keyboard` syntax (`{Enter}`, `{ArrowDown}`, `{Escape}`, `a`,
 * ` `), `{Tab}` moves focus forward.
 */
export interface KeyStep {
  keys: string
  then: Check[]
}

/** One inventory component's keyboard behavior. */
export interface KeyboardScript {
  /** The key of the example in `src/dev/examples.tsx`. */
  example: string
  /** The inventory component the script covers. */
  component: string
  /** A selector for the element the script starts on; the test focuses it first. Omit to start at the page. */
  start?: string
  steps: KeyStep[]
}

export const keyboardScripts: readonly KeyboardScript[] = [
  {
    example: 'button',
    component: 'Button',
    start: '[data-testid=button]',
    steps: [
      { keys: '{Enter}', then: [{ text: { selector: '[data-testid=count]', contains: '1' } }] },
      { keys: ' ', then: [{ text: { selector: '[data-testid=count]', contains: '2' } }] },
    ],
  },
  {
    // aria-disabled with a reason: still focusable, does nothing, reason described.
    example: 'button',
    component: 'Button (disabled with reason)',
    start: '[data-testid=button-reason]',
    steps: [
      {
        keys: '{Enter}',
        then: [
          { focused: '[data-testid=button-reason]' },
          {
            attribute: {
              selector: '[data-testid=button-reason]',
              name: 'aria-disabled',
              value: 'true',
            },
          },
        ],
      },
    ],
  },
  {
    example: 'input',
    component: 'Input, Textarea',
    start: '[data-testid=input]',
    steps: [
      { keys: 'ab', then: [{ value: { selector: '[data-testid=input]', is: 'ab' } }] },
      { keys: '{Tab}', then: [{ focused: '[aria-label=Invalid]' }] },
    ],
  },
  {
    example: 'select',
    component: 'Select',
    start: '[data-testid=select]',
    steps: [
      {
        keys: '{Enter}',
        then: [
          { attribute: { selector: '[data-testid=select]', name: 'aria-expanded', value: 'true' } },
        ],
      },
      {
        keys: '{ArrowDown}{Enter}',
        then: [
          {
            attribute: { selector: '[data-testid=select]', name: 'aria-expanded', value: 'false' },
          },
        ],
      },
    ],
  },
  {
    example: 'select',
    component: 'Select (Escape)',
    start: '[data-testid=select]',
    steps: [
      {
        keys: '{Enter}',
        then: [
          { attribute: { selector: '[data-testid=select]', name: 'aria-expanded', value: 'true' } },
        ],
      },
      {
        keys: '{Escape}',
        then: [
          {
            attribute: { selector: '[data-testid=select]', name: 'aria-expanded', value: 'false' },
          },
          { focused: '[data-testid=select]' },
        ],
      },
    ],
  },
  {
    example: 'checkbox-switch',
    component: 'Checkbox, Switch',
    start: '[data-testid=checkbox]',
    steps: [
      {
        keys: ' ',
        then: [
          {
            attribute: { selector: '[data-testid=checkbox]', name: 'aria-checked', value: 'true' },
          },
        ],
      },
      {
        keys: ' ',
        then: [
          {
            attribute: { selector: '[data-testid=checkbox]', name: 'aria-checked', value: 'false' },
          },
        ],
      },
    ],
  },
  {
    example: 'checkbox-switch',
    component: 'Switch',
    start: '[data-testid=switch]',
    steps: [
      {
        keys: ' ',
        then: [
          { attribute: { selector: '[data-testid=switch]', name: 'aria-checked', value: 'true' } },
        ],
      },
    ],
  },
  {
    example: 'field',
    component: 'Field',
    start: '[data-testid=field-input]',
    steps: [
      {
        keys: '{Tab}',
        then: [
          {
            attribute: {
              selector: '#field-bad',
              name: 'aria-describedby',
              value: 'field-bad-error',
            },
          },
        ],
      },
    ],
  },
  {
    example: 'form',
    component: 'Form',
    start: '[data-testid=form-input]',
    steps: [
      {
        keys: '{Enter}',
        then: [
          { text: { selector: '[data-testid=form]', contains: 'Name is required' } },
          {
            attribute: {
              selector: '[data-testid=form-input]',
              name: 'aria-invalid',
              value: 'true',
            },
          },
        ],
      },
      {
        keys: 'taken{Enter}',
        then: [{ text: { selector: '[data-testid=form]', contains: 'That name is taken.' } }],
      },
    ],
  },
  { example: 'form-alert', component: 'Form alert', steps: [] },
  {
    example: 'dialog',
    component: 'Dialog',
    start: '[data-testid=dialog-trigger]',
    steps: [
      { keys: '{Enter}', then: [{ present: '[data-testid=dialog]' }] },
      {
        keys: '{Escape}',
        then: [{ absent: '[data-testid=dialog]' }, { focused: '[data-testid=dialog-trigger]' }],
      },
    ],
  },
  {
    example: 'confirm-dialog',
    component: 'Confirm dialog',
    start: '[data-testid=confirm-trigger]',
    steps: [
      {
        keys: '{Enter}',
        then: [
          { present: '[role=alertdialog]' },
          { text: { selector: '[data-slot=alert-dialog-cancel]:focus', contains: 'Cancel' } },
        ],
      },
      {
        keys: '{Escape}',
        then: [{ absent: '[role=alertdialog]' }, { focused: '[data-testid=confirm-trigger]' }],
      },
    ],
  },
  {
    example: 'dropdown-menu',
    component: 'Dropdown menu',
    start: '[data-testid=menu-trigger]',
    steps: [
      { keys: '{Enter}', then: [{ present: '[data-testid=menu]' }] },
      { keys: '{ArrowDown}', then: [{ present: '[data-testid=menu] [data-highlighted]' }] },
      {
        keys: '{Escape}',
        then: [{ absent: '[data-testid=menu]' }, { focused: '[data-testid=menu-trigger]' }],
      },
    ],
  },
  {
    example: 'popover',
    component: 'Popover',
    start: '[data-testid=popover-trigger]',
    steps: [
      { keys: '{Enter}', then: [{ present: '[data-testid=popover]' }] },
      {
        keys: '{Escape}',
        then: [{ absent: '[data-testid=popover]' }, { focused: '[data-testid=popover-trigger]' }],
      },
    ],
  },
  {
    example: 'combobox',
    component: 'Combobox',
    start: '[data-testid=combobox]',
    steps: [
      { keys: 'ap', then: [{ present: '[data-testid=combobox-list]' }] },
      {
        keys: '{ArrowDown}{Enter}',
        then: [{ value: { selector: '[data-testid=combobox]', is: 'Apple' } }],
      },
    ],
  },
  {
    example: 'tooltip',
    component: 'Tooltip',
    start: '[data-testid=tooltip-trigger]',
    steps: [
      { keys: '{Tab}{Shift>}{Tab}{/Shift}', then: [{ present: '[data-testid=tooltip]' }] },
      { keys: '{Escape}', then: [{ absent: '[data-testid=tooltip]' }] },
    ],
  },
  {
    example: 'tabs',
    component: 'Tabs',
    start: '[data-testid=tab-one]',
    steps: [
      {
        keys: '{ArrowRight}',
        then: [
          { focused: '[data-testid=tab-two]' },
          {
            attribute: { selector: '[data-testid=tab-two]', name: 'aria-selected', value: 'true' },
          },
        ],
      },
    ],
  },
  {
    example: 'toast',
    component: 'Toast',
    start: '[data-testid=toast-success]',
    steps: [
      {
        keys: '{Enter}',
        then: [{ text: { selector: 'body', contains: 'Your changes are live.' } }],
      },
    ],
  },
  {
    example: 'toast',
    component: 'Toast (error stays)',
    start: '[data-testid=toast-error]',
    steps: [
      { keys: '{Enter}', then: [{ text: { selector: 'body', contains: 'Could not save' } }] },
      // F6 moves to the toast region, Tab reaches the toast, Escape dismisses it.
      { keys: '{F6}', then: [{ focused: '[aria-label=Notifications]' }] },
      { keys: '{Tab}', then: [{ focused: '[role=dialog]' }] },
      { keys: '{Escape}', then: [{ absent: '[role=dialog]' }] },
    ],
  },
  {
    example: 'table',
    component: 'Table, DataTable',
    start: '[data-testid=table] [role=region]',
    steps: [
      { keys: '{Tab}', then: [{ focused: '[data-testid=table] th button' }] },
      {
        keys: '{Enter}',
        then: [
          {
            attribute: {
              selector: '[data-testid=table] th[aria-sort]',
              name: 'aria-sort',
              value: 'ascending',
            },
          },
        ],
      },
    ],
  },
  { example: 'badge', component: 'Badge', steps: [] },
  {
    example: 'card',
    component: 'Card',
    start: '[data-testid=card-link]',
    steps: [{ keys: '{Tab}', then: [] }],
  },
  { example: 'empty', component: 'Empty state', steps: [] },
  { example: 'loading', component: 'Skeleton, Spinner', steps: [] },
  {
    example: 'breadcrumb',
    component: 'Breadcrumb',
    start: '[data-testid=crumb]',
    steps: [{ keys: '{Tab}', then: [{ text: { selector: ':focus', contains: 'Fixtures' } }] }],
  },
  {
    example: 'sidebar',
    component: 'Sidebar',
    start: 'nav a',
    steps: [
      {
        keys: '[[',
        then: [
          { attribute: { selector: '[data-slot=sidebar]', name: 'data-state', value: 'rail' } },
        ],
      },
      {
        keys: '[[',
        then: [
          { attribute: { selector: '[data-slot=sidebar]', name: 'data-state', value: 'expanded' } },
        ],
      },
    ],
  },
  { example: 'separator-kbd', component: 'Separator, Kbd', steps: [] },
  {
    example: 'copy-button',
    component: 'Copy button',
    start: '[data-testid=copy] button',
    steps: [
      {
        keys: '{Enter}',
        then: [{ text: { selector: '[data-testid=copy] [role=status]', contains: 'Copied' } }],
      },
    ],
  },
  {
    example: 'code-block',
    component: 'Code block',
    start: '[aria-label="install command"]',
    steps: [{ keys: '{Tab}', then: [{ focused: '[aria-label="Copy install command"]' }] }],
  },
]
