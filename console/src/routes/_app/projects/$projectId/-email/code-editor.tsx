import { defaultKeymap, history, historyKeymap, indentWithTab } from '@codemirror/commands'
import { html } from '@codemirror/lang-html'
import { HighlightStyle, syntaxHighlighting } from '@codemirror/language'
import { EditorState } from '@codemirror/state'
import { EditorView, keymap, lineNumbers, placeholder as placeholderText } from '@codemirror/view'
import { tags } from '@lezer/highlight'
import { useEffect, useRef } from 'react'

import { cn } from '@/lib/utils'

/** The editor's look, from the design tokens only, so it follows the theme and the density. */
const theme = EditorView.theme({
  '&': {
    height: '100%',
    color: 'var(--foreground)',
    backgroundColor: 'var(--background)',
    fontSize: 'var(--fs-mono)',
  },
  '&.cm-focused': { outline: 'none' },
  '.cm-scroller': {
    fontFamily: 'var(--font-mono)',
    lineHeight: 'var(--lh-mono)',
    overflow: 'auto',
  },
  '.cm-content': { padding: '0.5rem 0', caretColor: 'var(--foreground)' },
  '.cm-line': { padding: '0 0.625rem' },
  '.cm-gutters': {
    color: 'var(--muted-foreground)',
    backgroundColor: 'var(--muted)',
    border: 'none',
    borderRight: '1px solid var(--border)',
  },
  '.cm-cursor, .cm-dropCursor': { borderLeftColor: 'var(--foreground)' },
  '&.cm-focused > .cm-scroller > .cm-selectionLayer .cm-selectionBackground, .cm-selectionBackground, .cm-content ::selection':
    { backgroundColor: 'var(--accent)' },
  '.cm-placeholder': { color: 'var(--muted-foreground)' },
})

/** Syntax colors from the text tokens, which already meet AA on the page surface in both themes. */
const highlight = HighlightStyle.define([
  { tag: [tags.tagName, tags.angleBracket, tags.keyword], color: 'var(--link)' },
  { tag: [tags.attributeName, tags.propertyName], color: 'var(--warning-text)' },
  { tag: [tags.string, tags.attributeValue], color: 'var(--success-text)' },
  {
    tag: [tags.comment, tags.documentMeta, tags.processingInstruction],
    color: 'var(--muted-foreground)',
  },
])

/**
 * A CodeMirror editor (spec 0009, AC-9 and AC-30). It mounts in a shadow root: there CodeMirror puts
 * its styles in a constructed stylesheet, which the console's Content Security Policy allows, where
 * a `<style>` element in the page would be refused. The design tokens are custom properties, so they
 * still reach it.
 *
 * It is labelled by `label` (keep a visible label with the same words beside it), Tab indents, and
 * Escape then Tab leaves it. The document starts as `value` and is not controlled after that: give
 * it a new `key` to replace the text from outside.
 */
export function CodeEditor({
  label,
  value,
  onChange,
  language,
  placeholder,
  readOnly = false,
  invalid = false,
  className,
}: {
  /** The accessible name, the same words as the visible label. */
  label: string
  /** The starting text. */
  value: string
  onChange: (value: string) => void
  /** `html` colors tags and attributes; `text` is plain. */
  language: 'html' | 'text'
  /** Shown while the editor is empty. */
  placeholder?: string
  readOnly?: boolean
  /** Marks the editor as holding an error, for assistive tech and the border. */
  invalid?: boolean
  /** Sizes the editor: give it a height. */
  className?: string
}) {
  const host = useRef<HTMLDivElement | null>(null)
  const view = useRef<EditorView | null>(null)
  const latest = useRef({ value, onChange })
  useEffect(() => {
    latest.current.onChange = onChange
  }, [onChange])

  useEffect(() => {
    const element = host.current
    if (element === null) return
    const root = element.shadowRoot ?? element.attachShadow({ mode: 'open' })
    const parent = document.createElement('div')
    parent.style.height = '100%'
    root.append(parent)
    const editor = new EditorView({
      root,
      parent,
      state: EditorState.create({
        doc: latest.current.value,
        extensions: [
          lineNumbers(),
          history(),
          keymap.of([...defaultKeymap, ...historyKeymap, indentWithTab]),
          EditorView.lineWrapping,
          EditorState.readOnly.of(readOnly),
          EditorView.contentAttributes.of({
            'aria-label': label,
            'aria-multiline': 'true',
            // Already a tab stop as contenteditable; axe counts only a tabindex as focusable
            // content, so a scrolling editor fails scrollable-region-focusable without it.
            tabindex: '0',
            ...(readOnly ? { 'aria-readonly': 'true' } : {}),
          }),
          ...(placeholder === undefined ? [] : [placeholderText(placeholder)]),
          ...(language === 'html' ? [html()] : []),
          syntaxHighlighting(highlight),
          theme,
          EditorView.updateListener.of((update) => {
            if (!update.docChanged) return
            // Kept, so a rebuild (a changed label or mode) starts from what was typed.
            latest.current.value = update.state.doc.toString()
            latest.current.onChange(latest.current.value)
          }),
        ],
      }),
    })
    view.current = editor
    return () => {
      editor.destroy()
      parent.remove()
      view.current = null
    }
  }, [label, language, placeholder, readOnly])

  // The error state changes while the person types, so it never rebuilds the editor.
  useEffect(() => {
    view.current?.contentDOM.setAttribute('aria-invalid', invalid ? 'true' : 'false')
  }, [invalid])

  return (
    <div
      ref={host}
      data-slot="code-editor"
      data-invalid={invalid || undefined}
      className={cn(
        'overflow-hidden rounded-md border border-input bg-background focus-within:outline-2 focus-within:outline-ring data-[invalid=true]:border-destructive',
        className,
      )}
    />
  )
}
