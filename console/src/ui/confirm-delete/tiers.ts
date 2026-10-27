import { msg } from "@lingui/core/macro";
import type { MessageDescriptor } from "@lingui/core";

// Spec 0015 PRM-6, spec 0001 UX-23: the delete tier of every resource kind. A kind missing here fails the type check.

/** Spec 0001 UX-23's resource kinds, snake_case like spec 0014 CPX-2's dependent kinds. */
export type ResourceKind =
  | "organization"
  | "project"
  | "database"
  | "bucket"
  | "table"
  | "column"
  | "index"
  | "function"
  | "deployment"
  | "site"
  | "domain"
  | "api_key"
  | "webhook"
  | "messaging_provider"
  | "messaging_topic"
  | "end_user"
  | "console_member"
  | "backup"
  | "file";

/** Tier 1: type the name to confirm (holds resources that hold data). Tier 2: confirm. */
export const tiers: Record<ResourceKind, 1 | 2> = {
  organization: 1,
  project: 1,
  database: 1,
  bucket: 1,
  table: 2,
  column: 2,
  index: 2,
  function: 2,
  deployment: 2,
  site: 2,
  domain: 2,
  api_key: 2,
  webhook: 2,
  messaging_provider: 2,
  messaging_topic: 2,
  end_user: 2,
  console_member: 2,
  backup: 2,
  file: 2,
};

/** The delete button's specific label (UX-23): "Delete project", never "Delete" or "OK". */
export const deleteLabels: Record<ResourceKind, MessageDescriptor> = {
  organization: msg`Delete organization`,
  project: msg`Delete project`,
  database: msg`Delete database`,
  bucket: msg`Delete bucket`,
  table: msg`Delete table`,
  column: msg`Delete column`,
  index: msg`Delete index`,
  function: msg`Delete function`,
  deployment: msg`Delete deployment`,
  site: msg`Delete site`,
  domain: msg`Delete domain`,
  api_key: msg`Delete API key`,
  webhook: msg`Delete webhook`,
  messaging_provider: msg`Delete provider`,
  messaging_topic: msg`Delete topic`,
  end_user: msg`Delete user`,
  console_member: msg`Remove member`,
  backup: msg`Delete backup`,
  file: msg`Delete file`,
};
