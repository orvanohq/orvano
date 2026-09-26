// Runs before the first paint (spec 0005, Theme bootstrap): reads the two saved preferences and
// sets data-theme, data-density, and color-scheme on <html>. A classic script served from the
// console's own origin, so the Content Security Policy needs no hash for it. Keep it dependency
// free; PreferencesProvider takes over once React starts.
;(function () {
  var root = document.documentElement
  function read(key, allowed, fallback) {
    try {
      var value = window.localStorage.getItem(key)
      return allowed.indexOf(value) === -1 ? fallback : value
    } catch (_) {
      return fallback
    }
  }
  var theme = read('orvano.theme', ['dark', 'light', 'system'], 'dark')
  if (theme === 'system') {
    theme = window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
  }
  root.setAttribute('data-theme', theme)
  root.setAttribute('data-density', read('orvano.density', ['compact', 'comfortable'], 'compact'))
  root.style.colorScheme = theme
})()
