import { Link } from "@tanstack/react-router";
import { useGalleryText } from "../pseudo";
import { useSceneTitle } from "./scene";

const scenes = [
  { to: "/primitives", label: "Primitives" },
  { to: "/forms", label: "Forms" },
  { to: "/states", label: "Field states (default, help text, invalid, read only, disabled)" },
  { to: "/overlays", label: "Overlays (dialog, side sheet, confirm delete)" },
  { to: "/feedback", label: "Feedback and states (toasts, banners, skeletons, chips)" },
  { to: "/data", label: "Data display (table, settings, copy, secrets, time)" },
  { to: "/boot-error", label: "Boot error page" },
  { to: "/root-error", label: "Root error page" },
  { to: "/frame-skeleton", label: "Frame skeleton (before the shell can render)" },
  { to: "/acme/storefront/databases", label: "Shell, project scope (with a resource list)" },
  { to: "/acme/storefront/databases/users", label: "Shell, a table open (page header, tabs)" },
  { to: "/acme/storefront", label: "Shell, project scope (Overview, with a banner)" },
  { to: "/acme", label: "Shell, organization scope" },
  { to: "/acme/members", label: "Shell, organization members (a table)" },
  { to: "/acme/settings", label: "Shell, organization settings (settings and danger zone)" },
  { to: "/account/profile", label: "Shell, account scope" },
  { to: "/auth-layout", label: "AuthLayout (sign in)" },
] as const;

/** The list of scenes. Every primitive in every state, the shell in every scope (GAL-2). */
export function IndexScene() {
  const g = useGalleryText();
  useSceneTitle("Scenes");
  return (
    <main className="mx-auto flex max-w-content flex-col gap-4 p-6">
      <h1 className="text-xl font-semibold">{g("Orvano gallery")}</h1>
      <p className="text-fg-muted">
        {g(
          "Every primitive and the shell, on placeholder data. Use the toolbar to switch theme, density, locale, direction, width, and the fixtures' scenario: Slow, Fail, and Empty show the switchers', the resource list's, and the combobox's loading, error, and empty states. Reset gallery forgets what the gallery stored, before a screenshot.",
        )}
      </p>
      <p className="text-fg-muted">
        {g(
          "Reduced motion, more contrast, and forced colors: emulate them in your browser's rendering panel (Chrome and Edge: DevTools, More tools, Rendering). The primitives scene holds buttons in their hover, pressed, and focus states.",
        )}
      </p>
      <ul className="flex list-disc flex-col gap-1 ps-5">
        {scenes.map((scene) => (
          <li key={scene.to}>
            <Link to={scene.to as "/"} className="text-accent-text underline">
              {g(scene.label)}
            </Link>
          </li>
        ))}
      </ul>
    </main>
  );
}
