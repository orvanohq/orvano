// Spec 0015 ICN-1: the curated Lucide set under Orvano names. Feature code imports icons from here (only src/ui
// imports lucide-react); adding one is a one line change. Render them through `Icon` (ICN-2).
export type { LucideIcon as IconComponent } from "lucide-react";
export {
  // Project services (design.md _Iconography_)
  LayoutDashboard as ServiceOverviewIcon,
  Users as ServiceAuthIcon,
  Database as ServiceDatabasesIcon,
  HardDrive as ServiceStorageIcon,
  Radio as ServiceRealtimeIcon,
  SquareFunction as ServiceFunctionsIcon,
  Globe as ServiceSitesIcon,
  Send as ServiceMessagingIcon,
  ChartColumn as ServiceUsageIcon,
  ScrollText as ServiceLogsIcon,
  Settings as ServiceSettingsIcon,
  // Organization and account pages
  FolderKanban as OrgProjectsIcon,
  UsersRound as OrgMembersIcon,
  Building2 as OrgSettingsIcon,
  CircleUser as AccountProfileIcon,
  ShieldCheck as AccountSecurityIcon,
  SlidersHorizontal as AccountPreferencesIcon,
  // Direction icons: render with `flipInRtl`
  ChevronLeft as ChevronStartIcon,
  ChevronRight as ChevronEndIcon,
  ChevronsLeft as CollapseStartIcon,
  ChevronsRight as ExpandEndIcon,
  // Controls
  Menu as SidebarOpenIcon,
  X as CloseIcon,
  Check as CheckIcon,
  ChevronDown as DisclosureIcon,
  Ellipsis as MoreIcon,
  Plus as AddIcon,
  Search as SearchIcon,
  Keyboard as ShortcutsIcon,
  LogOut as SignOutIcon,
} from "lucide-react";
