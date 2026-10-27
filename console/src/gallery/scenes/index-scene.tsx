import { Link } from "@tanstack/react-router";

const scenes = [
  { to: "/primitives", label: "Primitives" },
  { to: "/forms", label: "Forms" },
  { to: "/overlays", label: "Overlays (dialog, side sheet, confirm delete)" },
  { to: "/acme/storefront/databases", label: "Shell, project scope (with a resource list)" },
  { to: "/acme/storefront/databases/users", label: "Shell, a table open (page header, tabs)" },
  { to: "/acme/storefront", label: "Shell, project scope (Overview)" },
  { to: "/acme", label: "Shell, organization scope" },
  { to: "/account/profile", label: "Shell, account scope" },
  { to: "/auth-layout", label: "AuthLayout" },
] as const;

/** The list of scenes. Every primitive in every state, the shell in every scope (GAL-2). */
export function IndexScene() {
  return (
    <main className="mx-auto flex max-w-content flex-col gap-4 p-6">
      <h1 className="text-xl font-semibold">Orvano gallery</h1>
      <p className="text-fg-muted">
        Every primitive and the shell, on placeholder data. Use the toolbar to switch theme,
        density, locale, direction, and width.
      </p>
      <ul className="flex list-disc flex-col gap-1 ps-5">
        {scenes.map((scene) => (
          <li key={scene.to}>
            <Link to={scene.to as "/"} className="text-accent-text underline">
              {scene.label}
            </Link>
          </li>
        ))}
      </ul>
    </main>
  );
}
