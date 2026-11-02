// The projects area's public surface (spec 0003 DIR-1): other areas import only from here.
export {
  projectQuery,
  projectsFilter,
  projectsQuery,
  searchProjects,
  type Project,
} from "./api/queries";
export { ConnectPanel } from "./components/lazy-connect-panel";
