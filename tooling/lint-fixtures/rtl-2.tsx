// lint-as: console/src/ui/shell/primary-sidebar.tsx
const variants = { start: "ml-2 data-hovered:pr-4", corner: "rounded-tl-md" };

export function Sidebar() {
  return <nav className={`border-r text-left lg:-left-2 ${variants.start}`} />;
}
