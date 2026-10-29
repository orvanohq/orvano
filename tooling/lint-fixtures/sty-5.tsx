// lint-as: console/src/features/tables/components/rows-page.tsx
declare const cx: (...classes: string[]) => string;
declare const tone: string;

export function RowsPage() {
  return (
    <div style={{ width: 13 }}>
      <span className="w-[13px] text-fg">Rows</span>
      <span className="[mask-type:luminance]">Mask</span>
      <span className="hover:[mask-type:luminance]">Variant</span>
      <span className="[&>svg]:size-4">Arbitrary variant</span>
      <span className={cx("text-fg", `${tone} w-[13px]`)}>Template</span>
    </div>
  );
}
