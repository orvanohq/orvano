// The design system (spec 0015). Feature code imports primitives from here; the lazy ones in PRM-10 are imported
// by path instead.
export { Avatar, type AvatarProps } from "./avatar";
export {
  Button,
  IconButton,
  type ButtonProps,
  type ButtonSize,
  type ButtonVariant,
  type IconButtonProps,
} from "./button";
export { Icon, type IconProps } from "./icon";
export * from "./icons";
export { Link } from "./link";
export { Tooltip, type TooltipProps } from "./tooltip";
export { VisuallyHidden } from "./visually-hidden";
export { AppShell } from "./shell/app-shell";
export { AuthLayout } from "./shell/auth-layout";
export { BrandLink, BrandMark } from "./shell/brand-mark";
export { navRegistry, type NavId } from "./shell/nav-registry";
export { PrimarySidebar } from "./shell/primary-sidebar";
export { SkipLink } from "./shell/skip-link";
export type * from "./shell/types";
export { LocaleProvider } from "./locale-provider";
