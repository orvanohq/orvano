import type { BadgeVariant } from "@/components/ui/badge";

/** Sample webhook deliveries for the kit table. Built in code from a seed, so the page holds no real data. */
export type Delivery = {
  id: string;
  endpoint: string;
  status: { label: string; variant: BadgeVariant };
  latencyMs: number;
  sizeKb: number;
  attempts: number;
};

const endpoints = [
  "https://recipes.example.com/hooks/orders",
  "https://api.field-notes.dev/webhooks/orvano",
  "https://hooks.internal-tools.io/sync",
];

const statuses: Delivery["status"][] = [
  { label: "Delivered", variant: "success" },
  { label: "Delivered", variant: "success" },
  { label: "Retrying", variant: "warning" },
  { label: "Delivered", variant: "success" },
  { label: "Failed", variant: "danger" },
];

export const deliveries: Delivery[] = Array.from({ length: 25 }, (_, index) => {
  const n = index + 1;
  return {
    id: `dlv_01JF8K2M4Q${String(n).padStart(2, "0")}`,
    endpoint: endpoints[index % endpoints.length],
    status: statuses[index % statuses.length],
    // 12 to 480 ms, spread without randomness so every render matches.
    latencyMs: 12 + ((index * 97) % 469),
    sizeKb: Math.round((0.4 + ((index * 37) % 120) / 10) * 10) / 10,
    attempts:
      statuses[index % statuses.length].variant === "success"
        ? 1
        : 1 + (index % 3) + 1,
  };
});
