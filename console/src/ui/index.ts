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
export { Dialog, DialogTrigger, type DialogProps, type DialogTriggerProps } from "./dialog";
export { Icon, type IconProps } from "./icon";
export * from "./icons";
export { Link } from "./link";
export { Menu, MenuItem, MenuSection, MenuSeparator, MenuTrigger } from "./menu";
export type { MenuItemProps, MenuProps, MenuSectionProps, MenuTriggerProps } from "./menu";
export { PageHeader, type PageHeaderProps } from "./page-header";
export { Popover, type PopoverProps } from "./popover";
export { RouteTabs, type RouteTab, type RouteTabsProps } from "./route-tabs";
export { Tab, TabList, TabPanel, Tabs, type TabsProps } from "./tabs";
export { Tooltip, type TooltipProps } from "./tooltip";
export { VisuallyHidden } from "./visually-hidden";
export { announce } from "./announcer";
export { AccountMenu, type AccountMenuProps } from "./shell/account-menu";
export { AppShell } from "./shell/app-shell";
export { Breadcrumbs, type BreadcrumbsProps } from "./shell/breadcrumbs";
export { Drawer, type DrawerProps } from "./shell/drawer";
export { OrgSwitcher, ProjectSwitcher, type SwitcherProps } from "./shell/switcher";
export { ResourceLayout, type ResourceLayoutProps } from "./shell/resource-layout";
export { ResourceList, type ResourceListItem, type ResourceListProps } from "./shell/resource-list";
export { documentTitle, useDocumentTitle } from "./shell/use-document-title";
export { useIsWide } from "./shell/use-is-wide";
export { AuthLayout } from "./shell/auth-layout";
export { BrandLink, BrandMark } from "./shell/brand-mark";
export { navRegistry, type NavId } from "./shell/nav-registry";
export { PrimarySidebar } from "./shell/primary-sidebar";
export { SkipLink } from "./shell/skip-link";
export type * from "./shell/types";
export { LocaleProvider } from "./locale-provider";
