// Spec 0015 THM-2, spec 0003 STO-6: sets the theme before the first paint, so no page flashes the wrong one. A
// classic script file (the CSP allows no inline script). It reads the one theme key and does nothing else; if it
// fails or storage is blocked, theme.css follows the OS through CSS alone (THM-3).
try {
  const preference = JSON.parse(localStorage.getItem("orvano:v1:theme") || '"system"');
  const theme =
    preference === "light" || preference === "dark"
      ? preference
      : matchMedia("(prefers-color-scheme: dark)").matches
        ? "dark"
        : "light";
  document.documentElement.setAttribute("data-theme", theme);
  document.documentElement.style.colorScheme = theme;
} catch {
  // Leave <html> alone: CSS follows the OS theme.
}
