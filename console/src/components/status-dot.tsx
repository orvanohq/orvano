import { cn } from "cn";

export type Status = "success" | "warning" | "danger";

const dotColour: Record<Status, string> = {
  success: "bg-success",
  warning: "bg-warning",
  danger: "bg-danger",
};

/** An 8px dot in the status colour, always followed by its word in ink, so status is never colour alone. */
export function StatusDot({
  status,
  children,
  className,
}: {
  status: Status;
  children: string;
  className?: string;
}) {
  return (
    <span
      data-slot="status-dot"
      className={cn(
        "inline-flex items-center gap-1.5 text-sm text-ink",
        className,
      )}
    >
      <span
        aria-hidden="true"
        className={cn("size-2 shrink-0 rounded-full", dotColour[status])}
      />
      {children}
    </span>
  );
}
