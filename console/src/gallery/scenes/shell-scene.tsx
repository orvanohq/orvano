import { AppShell, type ShellScope } from "@/ui";
import { crumbs, shellModel } from "../fixtures";

/** The shell in one scope, on placeholder data (GAL-2). */
export function ShellScene({
  scope,
  page,
  withResourceList = false,
}: {
  scope: ShellScope;
  page: string;
  withResourceList?: boolean;
}) {
  const title = page.charAt(0).toUpperCase() + page.slice(1);
  return (
    <AppShell
      model={shellModel(scope)}
      crumbs={crumbs(scope, title)}
      resourceList={
        withResourceList ? (
          <nav aria-label="Tables" className="w-resource-list shrink-0 border-e bg-bg-subtle p-3">
            <p className="text-sm text-fg-muted">The resource list arrives with milestone 2.</p>
          </nav>
        ) : undefined
      }
    >
      <h1 className="text-xl font-semibold">{title}</h1>
      <p className="mt-2 max-w-content text-fg-muted">
        Placeholder content for the {scope} scope. Page headers, tabs, and real screens come later.
      </p>
    </AppShell>
  );
}
