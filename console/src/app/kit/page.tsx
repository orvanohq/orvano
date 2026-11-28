import type { Metadata } from "next";

import { kitTitle } from "@/shell/nav";

import { Kit } from "./kit";

export const metadata: Metadata = { title: kitTitle };

/** Every base component and state, for tests and reference. Linked from neither rail (spec 0003, AC-12). */
export default function KitPage() {
  return <Kit />;
}
