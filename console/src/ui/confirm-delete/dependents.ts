import { msg, plural } from "@lingui/core/macro";
import type { I18n, MessageDescriptor } from "@lingui/core";
import type { ResourceKind } from "./tiers";

/** Spec 0014 CPX-2: one line of a resource's dependents endpoint. `kind` is a snake_case resource name. */
export interface Dependent {
  kind: string;
  count: number;
  /** The count is an estimate (a large table's rows): shown as "about". */
  estimated: boolean;
}

export type LoadDependents = (signal: AbortSignal) => Promise<{ items: Dependent[] }>;

type Counted = (count: number) => MessageDescriptor;

/** "12 tables": every resource kind, plus `row` (the rows a table or column takes with it). */
const counted: Record<ResourceKind | "row", Counted> = {
  organization: (count) =>
    msg`${plural(count, { one: "# organization", other: "# organizations" })}`,
  project: (count) => msg`${plural(count, { one: "# project", other: "# projects" })}`,
  database: (count) => msg`${plural(count, { one: "# database", other: "# databases" })}`,
  bucket: (count) => msg`${plural(count, { one: "# bucket", other: "# buckets" })}`,
  table: (count) => msg`${plural(count, { one: "# table", other: "# tables" })}`,
  column: (count) => msg`${plural(count, { one: "# column", other: "# columns" })}`,
  index: (count) => msg`${plural(count, { one: "# index", other: "# indexes" })}`,
  function: (count) => msg`${plural(count, { one: "# function", other: "# functions" })}`,
  deployment: (count) => msg`${plural(count, { one: "# deployment", other: "# deployments" })}`,
  site: (count) => msg`${plural(count, { one: "# site", other: "# sites" })}`,
  domain: (count) => msg`${plural(count, { one: "# domain", other: "# domains" })}`,
  api_key: (count) => msg`${plural(count, { one: "# API key", other: "# API keys" })}`,
  webhook: (count) => msg`${plural(count, { one: "# webhook", other: "# webhooks" })}`,
  messaging_provider: (count) =>
    msg`${plural(count, { one: "# messaging provider", other: "# messaging providers" })}`,
  messaging_topic: (count) =>
    msg`${plural(count, { one: "# messaging topic", other: "# messaging topics" })}`,
  end_user: (count) => msg`${plural(count, { one: "# user", other: "# users" })}`,
  console_member: (count) => msg`${plural(count, { one: "# member", other: "# members" })}`,
  backup: (count) => msg`${plural(count, { one: "# backup", other: "# backups" })}`,
  file: (count) => msg`${plural(count, { one: "# file", other: "# files" })}`,
  row: (count) => msg`${plural(count, { one: "# row", other: "# rows" })}`,
};

/** The text for one dependent: "12 tables", or "about 1,200 rows" for an estimate (UX-23). */
export function dependentText(i18n: I18n, item: Dependent): string {
  const { count, kind } = item;
  const counts = Object.hasOwn(counted, kind)
    ? i18n._(counted[kind as keyof typeof counted](count))
    : i18n._(msg`${count} of kind ${kind}`);
  return item.estimated ? i18n._(msg`about ${counts}`) : counts;
}
