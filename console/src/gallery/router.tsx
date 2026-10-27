import {
  createRootRouteWithContext,
  createRoute,
  createRouter,
  Outlet,
  useRouter,
  type RouterHistory,
} from "@tanstack/react-router";
import { UnsavedChangesGuard } from "@/ui/form/unsaved-changes-guard";
import { Announcer } from "@/ui/announcer";
import { UiRouterProvider } from "@/ui/router-provider";
import { ToastRegion } from "@/ui/toast";
import { GalleryFrame } from "./frame";
import { BootErrorPage, RootErrorPage } from "@/ui";
import { AuthScene } from "./scenes/auth-scene";
import { DataScene } from "./scenes/data-scene";
import { FeedbackScene } from "./scenes/feedback-scene";
import { FormsScene } from "./scenes/forms-scene";
import { IndexScene } from "./scenes/index-scene";
import { OverlaysScene } from "./scenes/overlays-scene";
import { PrimitivesScene } from "./scenes/primitives-scene";
import { ShellScene } from "./scenes/shell-scene";

// Spec 0015 GAL-1: a code based router with hash history, so the app's routeTree.gen.ts never changes. It declares
// the app's shell paths, so active links match as they do in the app.

interface GalleryContext {
  /** False in tests and inside the narrow width frame: the scene renders without the toolbar. */
  toolbar: boolean;
}

const rootRoute = createRootRouteWithContext<GalleryContext>()({
  component: function GalleryRoot() {
    // The app's router is the registered one, so the gallery reads its own context untyped.
    const { toolbar } = useRouter().options.context as unknown as GalleryContext;
    return (
      <UiRouterProvider>
        {toolbar ? <GalleryFrame /> : <Outlet />}
        <UnsavedChangesGuard />
        <ToastRegion />
        <Announcer />
      </UiRouterProvider>
    );
  },
});

const indexRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/",
  component: IndexScene,
});

const primitivesRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/primitives",
  component: PrimitivesScene,
});

const formsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/forms",
  component: FormsScene,
});

const overlaysRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/overlays",
  component: OverlaysScene,
});

const feedbackRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/feedback",
  component: FeedbackScene,
});

const dataRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/data",
  component: DataScene,
});

// The plain error pages (FLT-2), each with a request ID as the API would give it.
const failure = { kind: "server", requestId: "req_01JB8Z4Q7W2N" };

const bootErrorRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/boot-error",
  component: () => <BootErrorPage error={failure} />,
});

const rootErrorRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/root-error",
  component: () => <RootErrorPage error={failure} />,
});

const authRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/auth-layout",
  component: AuthScene,
});

const accountRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/account/$page",
  component: function AccountScene() {
    const { page } = accountRoute.useParams();
    return <ShellScene scope="account" page={page} />;
  },
});

const orgRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/$orgId",
  component: () => <ShellScene scope="org" page="Projects" />,
});

const orgMembersRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/$orgId/members",
  component: () => <ShellScene scope="org" page="Members" />,
});

const orgSettingsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/$orgId/settings",
  component: () => <ShellScene scope="org" page="Organization settings" />,
});

const projectRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/$orgId/$projectId",
  component: () => <ShellScene scope="project" page="Overview" withBanner />,
});

const serviceRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/$orgId/$projectId/$service",
  component: function ServiceScene() {
    const { service } = serviceRoute.useParams();
    return <ShellScene scope="project" page={service} withResourceList={service === "databases"} />;
  },
});

const tableRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/$orgId/$projectId/databases/$tableId",
  component: function TableScene() {
    const { tableId } = tableRoute.useParams();
    return <ShellScene scope="project" page="databases" withResourceList tableId={tableId} />;
  },
});

const tableColumnsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/$orgId/$projectId/databases/$tableId/columns",
  component: function TableColumnsScene() {
    const { tableId } = tableColumnsRoute.useParams();
    return (
      <ShellScene
        scope="project"
        page="databases"
        withResourceList
        tableId={tableId}
        tab="columns"
      />
    );
  },
});

const routeTree = rootRoute.addChildren([
  indexRoute,
  primitivesRoute,
  formsRoute,
  overlaysRoute,
  feedbackRoute,
  dataRoute,
  bootErrorRoute,
  rootErrorRoute,
  authRoute,
  accountRoute,
  orgRoute,
  orgMembersRoute,
  orgSettingsRoute,
  projectRoute,
  serviceRoute,
  tableRoute,
  tableColumnsRoute,
]);

export function createGalleryRouter(options: { history: RouterHistory; toolbar: boolean }) {
  return createRouter({
    routeTree,
    history: options.history,
    context: { toolbar: options.toolbar },
  });
}
