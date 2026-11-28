import * as React from "react";
import { cva, type VariantProps } from "class-variance-authority";
import { cn } from "cn";
import { Slot } from "radix-ui";

/**
 * Badges: a -subtle background, caption type, radius-sm and an optional 6px dot in the strong colour.
 * Text on a -subtle background is brand-ink (brand) or ink (status), never the strong colour, which fails AA
 * on some pairs (spec 0003, "Contrast on subtle backgrounds"). The word is always there, so status is never colour alone.
 */
const badgeVariants = cva(
  "inline-flex h-5 w-fit shrink-0 items-center gap-1 overflow-hidden rounded-sm px-1.5 text-xs font-medium whitespace-nowrap",
  {
    variants: {
      variant: {
        neutral: "bg-surface-subtle text-ink-muted",
        brand: "bg-brand-subtle text-brand-ink",
        success: "bg-success-subtle text-ink",
        warning: "bg-warning-subtle text-ink",
        danger: "bg-danger-subtle text-ink",
      },
    },
    defaultVariants: {
      variant: "neutral",
    },
  },
);

const dotVariants = cva("size-1.5 shrink-0 rounded-full", {
  variants: {
    variant: {
      neutral: "bg-ink-subtle",
      brand: "bg-brand",
      success: "bg-success",
      warning: "bg-warning",
      danger: "bg-danger",
    },
  },
  defaultVariants: {
    variant: "neutral",
  },
});

function Badge({
  className,
  variant = "neutral",
  dot = false,
  asChild = false,
  children,
  ...props
}: React.ComponentProps<"span"> &
  VariantProps<typeof badgeVariants> & { asChild?: boolean; dot?: boolean }) {
  const Comp = asChild ? Slot.Root : "span";

  return (
    <Comp
      data-slot="badge"
      data-variant={variant}
      className={cn(badgeVariants({ variant }), className)}
      {...props}
    >
      {dot && (
        <span
          aria-hidden="true"
          data-slot="badge-dot"
          className={dotVariants({ variant })}
        />
      )}
      {children}
    </Comp>
  );
}

export { Badge, badgeVariants };
export type BadgeVariant = NonNullable<
  VariantProps<typeof badgeVariants>["variant"]
>;
