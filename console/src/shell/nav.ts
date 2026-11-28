import {
  ArrowLeft,
  Database,
  Folder,
  Globe,
  KeyRound,
  LayoutDashboard,
  LayoutGrid,
  List,
  Mail,
  Radio,
  ScrollText,
  Settings,
  Sparkles,
  SquareFunction,
  Users,
  Webhook,
  type LucideIcon,
} from "lucide-react";

/**
 * One rail destination. Rails, the drawer, stub pages and page titles all read this list (spec 0003, "Nav config").
 * A later feature that builds a page changes its own entry only.
 */
export type NavItem = {
  /** Short label in the rail tooltip and the drawer. */
  label: string;
  /** The page `<title>` and `h1`. */
  title: string;
  icon: LucideIcon;
  /** The route without the `/console` base path, with its trailing slash. */
  route: `/${string}`;
  /** The one line under the page title. */
  description: string;
  /** The milestone the real page arrives in, shown on the stub. */
  milestone: string;
};

export const projectsItem = {
  label: "Projects",
  title: "Projects",
  icon: LayoutGrid,
  route: "/",
  description:
    "The projects on this instance. Dev and production are separate projects.",
  milestone: "M1",
} as const satisfies NavItem;

export const membersItem = {
  label: "Members",
  title: "Members",
  icon: Users,
  route: "/members/",
  description: "People who can sign in to this instance, and their roles.",
  milestone: "M1",
} as const satisfies NavItem;

export const auditLogItem = {
  label: "Audit log",
  title: "Audit log",
  icon: List,
  route: "/audit-log/",
  description:
    "Every change made on this instance, by people, keys and agents.",
  milestone: "M1",
} as const satisfies NavItem;

export const instanceSettingsItem = {
  label: "Instance settings",
  title: "Instance settings",
  icon: Settings,
  route: "/settings/",
  description: "Email, domains, GitHub, backups and updates for this instance.",
  milestone: "M2",
} as const satisfies NavItem;

/** The instance rail, in order. */
export const instanceNav: readonly NavItem[] = [
  projectsItem,
  membersItem,
  auditLogItem,
  instanceSettingsItem,
];

/** The service rail, in the design system README order. Every link carries `?project=`. */
export const serviceNav = {
  overview: {
    label: "Overview",
    title: "Overview",
    icon: LayoutDashboard,
    route: "/project/overview/",
    description: "How this project is doing, and how to connect to it.",
    milestone: "M9",
  },
  auth: {
    label: "Auth",
    title: "Auth",
    icon: KeyRound,
    route: "/project/auth/",
    description: "Users, sign in methods and security settings.",
    milestone: "M2",
  },
  database: {
    label: "Database",
    title: "Database",
    icon: Database,
    route: "/project/database/",
    description: "Tables, schema, migrations and permissions.",
    milestone: "M3",
  },
  storage: {
    label: "Storage",
    title: "Storage",
    icon: Folder,
    route: "/project/storage/",
    description: "Buckets and files.",
    milestone: "M4",
  },
  realtime: {
    label: "Realtime",
    title: "Realtime",
    icon: Radio,
    route: "/project/realtime/",
    description: "Live events and connections.",
    milestone: "M4",
  },
  functions: {
    label: "Functions",
    title: "Functions",
    icon: SquareFunction,
    route: "/project/functions/",
    description: "Deployments, runs, triggers and secrets.",
    milestone: "M5",
  },
  sites: {
    label: "Sites",
    title: "Sites",
    icon: Globe,
    route: "/project/sites/",
    description: "Deployments, previews and domains.",
    milestone: "M8",
  },
  webhooks: {
    label: "Webhooks",
    title: "Webhooks",
    icon: Webhook,
    route: "/project/webhooks/",
    description: "Endpoints and delivery history.",
    milestone: "M5",
  },
  email: {
    label: "Email",
    title: "Email",
    icon: Mail,
    route: "/project/email/",
    description: "Templates, topics and delivery history.",
    milestone: "M5",
  },
  ai: {
    label: "AI",
    title: "AI",
    icon: Sparkles,
    route: "/project/ai/",
    description: "Connected agents, tokens and approvals.",
    milestone: "M7",
  },
  logs: {
    label: "Logs",
    title: "Logs",
    icon: ScrollText,
    route: "/project/logs/",
    description: "Search every log this project produces.",
    milestone: "M9",
  },
  settings: {
    label: "Settings",
    title: "Settings",
    icon: Settings,
    route: "/project/settings/",
    description: "Platforms, API keys, members and project export.",
    milestone: "M1",
  },
} as const satisfies Record<string, NavItem>;

export const serviceNavItems: readonly NavItem[] = Object.values(serviceNav);

/** The service rail's link back to Projects. It is never the current item. */
export const backToProjects = {
  label: "Projects",
  icon: ArrowLeft,
  route: "/",
} as const;

export const kitTitle = "Base components";
export const notFoundTitle = "Page not found";

/** Which rail a page shows: decided from the path alone, so the first paint already has it. */
export type RailKind = "instance" | "service" | "none";

/** Adds the trailing slash `usePathname()` may leave off, so routes compare exactly. */
export function normalisePath(pathname: string): string {
  return pathname.endsWith("/") ? pathname : `${pathname}/`;
}

export function railFor(pathname: string): RailKind {
  const path = normalisePath(pathname);
  if (instanceNav.some((item) => item.route === path)) {
    return "instance";
  }
  // Only known service routes: an unknown /project/… path is a not found page, which has no rail.
  if (serviceNavItems.some((item) => item.route === path)) {
    return "service";
  }
  return "none";
}

export function isCurrent(
  item: Pick<NavItem, "route">,
  pathname: string,
): boolean {
  return item.route === normalisePath(pathname);
}
