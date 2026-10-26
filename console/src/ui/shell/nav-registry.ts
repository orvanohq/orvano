import type { MessageDescriptor } from "@lingui/core";
import { msg } from "@lingui/core/macro";
import {
  AccountPreferencesIcon,
  AccountProfileIcon,
  AccountSecurityIcon,
  OrgMembersIcon,
  OrgProjectsIcon,
  OrgSettingsIcon,
  ServiceAuthIcon,
  ServiceDatabasesIcon,
  ServiceFunctionsIcon,
  ServiceLogsIcon,
  ServiceMessagingIcon,
  ServiceOverviewIcon,
  ServiceRealtimeIcon,
  ServiceSettingsIcon,
  ServiceSitesIcon,
  ServiceStorageIcon,
  ServiceUsageIcon,
  type IconComponent,
} from "../icons";

// Spec 0015 SHL-2: nav labels and icons belong to the design system, keyed by NavId, in UX-1 order. A NavId this
// registry doesn't know fails the type check.

export interface NavEntry {
  label: MessageDescriptor;
  icon: IconComponent;
}

export const navRegistry = {
  // Project scope
  overview: { label: msg`Overview`, icon: ServiceOverviewIcon },
  auth: { label: msg`Auth`, icon: ServiceAuthIcon },
  databases: { label: msg`Databases`, icon: ServiceDatabasesIcon },
  storage: { label: msg`Storage`, icon: ServiceStorageIcon },
  realtime: { label: msg`Realtime`, icon: ServiceRealtimeIcon },
  functions: { label: msg`Functions`, icon: ServiceFunctionsIcon },
  sites: { label: msg`Sites`, icon: ServiceSitesIcon },
  messaging: { label: msg`Messaging`, icon: ServiceMessagingIcon },
  usage: { label: msg`Usage`, icon: ServiceUsageIcon },
  logs: { label: msg`Logs`, icon: ServiceLogsIcon },
  settings: { label: msg`Settings`, icon: ServiceSettingsIcon },
  // Organization scope
  projects: { label: msg`Projects`, icon: OrgProjectsIcon },
  members: { label: msg`Members`, icon: OrgMembersIcon },
  "org-settings": { label: msg`Organization settings`, icon: OrgSettingsIcon },
  // Account scope
  profile: { label: msg`Profile`, icon: AccountProfileIcon },
  security: { label: msg`Security`, icon: AccountSecurityIcon },
  preferences: { label: msg`Preferences`, icon: AccountPreferencesIcon },
} as const satisfies Record<string, NavEntry>;

export type NavId = keyof typeof navRegistry;
