import { OrvanoError } from '@orvano/js'
import { currentUser, orvano } from './orvano'

const app: HTMLElement = document.querySelector('#app') ?? document.body

// #region render
/** Shows the signed in user, or the sign up and sign in forms. */
async function render(): Promise<void> {
  const user = await currentUser()
  if (user === null) {
    app.replaceChildren(authForms())
    return
  }

  const section = element('section', { 'aria-labelledby': 'signed-in' })
  section.append(
    element('h1', { id: 'signed-in' }, "You're signed in"),
    element('p', {}, `Name: ${user.name ?? 'No name'}`),
    element('p', {}, `Email: ${user.email ?? ''}`),
  )
  const signOut = element('button', { type: 'button' }, 'Sign out')
  signOut.addEventListener('click', () => {
    void orvano.account.deleteCurrentSession().then(render)
  })
  section.append(signOut)
  app.replaceChildren(section)
}
// #endregion render

// #region forms
/** A sign up form and a sign in form. Each shows Orvano's message when it fails. */
function authForms(): DocumentFragment {
  const fragment = document.createDocumentFragment()
  fragment.append(element('h1', {}, 'Orvano JavaScript quickstart'))

  fragment.append(
    form('sign-up', 'Sign up', ['name', 'email', 'password'], async (values) => {
      // Creates the user and signs them in; the client stores the session.
      await orvano.account.create({
        name: values.name,
        email: values.email,
        password: values.password,
      })
    }),
    form('sign-in', 'Sign in', ['email', 'password'], async (values) => {
      await orvano.account.createPasswordSession({
        email: values.email,
        password: values.password,
      })
    }),
  )
  return fragment
}

type Field = 'name' | 'email' | 'password'

function form(
  id: string,
  title: string,
  fields: Field[],
  submit: (values: Record<Field, string>) => Promise<void>,
): HTMLFormElement {
  const formElement = element('form', { 'aria-labelledby': id })
  formElement.append(element('h2', { id }, title))
  for (const field of fields) formElement.append(input(field, id))
  const button = element('button', { type: 'submit' }, title)
  const alert = element('p', { role: 'alert' })
  formElement.append(button, alert)

  formElement.addEventListener('submit', (event) => {
    event.preventDefault()
    const data = new FormData(formElement)
    const values = { name: '', email: '', password: '' }
    for (const field of fields) values[field] = String(data.get(field) ?? '')
    button.disabled = true
    submit(values)
      .then(render)
      .catch((error: unknown) => {
        // Orvano's message for an expected failure, like a taken email or a wrong password.
        if (!(error instanceof OrvanoError)) throw error
        alert.textContent = error.message
      })
      .finally(() => {
        button.disabled = false
      })
  })
  return formElement
}
// #endregion forms

const labels: Record<Field, string> = { name: 'Name', email: 'Email', password: 'Password' }

function input(field: Field, formId: string): HTMLLabelElement {
  const label = element('label', {}, `${labels[field]} `)
  label.append(
    element('input', {
      name: field,
      type: field === 'name' ? 'text' : field,
      autocomplete:
        field === 'password' ? (formId === 'sign-up' ? 'new-password' : 'current-password') : field,
      ...(field === 'name' ? {} : { required: '' }),
    }),
  )
  return label
}

function element<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  attributes: Record<string, string>,
  text?: string,
): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag)
  for (const [name, value] of Object.entries(attributes)) node.setAttribute(name, value)
  if (text !== undefined) node.textContent = text
  return node
}

// #region start
// Show the right screen now, and again when the session changes in another tab.
orvano.client.onAuthStateChange(() => void render())
void render()
// #endregion start
