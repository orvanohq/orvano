// Expressive Code for orvano.dev (spec 0011). It lives here, not in astro.config.mjs, because it holds a plugin
// function and Starlight's <Code> component (quickstarts, API reference, error pages) reads this file too.
import { defineEcConfig } from '@astrojs/starlight/expressive-code'
import { selectAll } from '@astrojs/starlight/expressive-code/hast'

/**
 * A long line makes a code block scroll sideways, and a scrolling region must be reachable from the keyboard
 * (WCAG 2.1.1, axe's `scrollable-region-focusable`, AC-7). Every `<pre>` joins the tab order.
 *
 * @type {import('@astrojs/starlight/expressive-code').ExpressiveCodePlugin}
 */
const focusableCode = {
  name: 'orvano-focusable-code',
  hooks: {
    postprocessRenderedBlock: ({ renderData }) => {
      for (const pre of selectAll('pre', renderData.blockAst)) pre.properties.tabIndex = 0
    },
  },
}

export default defineEcConfig({
  themes: ['github-dark-default', 'github-light-default'],
  styleOverrides: { codeFontFamily: 'var(--sl-font-mono)' },
  plugins: [focusableCode],
})
