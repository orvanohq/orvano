import { msg } from "@lingui/core/macro";
import { useLingui } from "@lingui/react/macro";
import { Button } from "../button";
import { Menu, MenuItem, MenuSection, MenuTrigger } from "../menu";

const noPage = msg`No more pages.`;

export interface PaginationProps {
  pageSize: number;
  /** What the picker offers (spec 0001 UX-14: 25, 50, 100). */
  pageSizes: readonly number[];
  onPageSizeChange: (pageSize: number) => void;
  /** Left out on the first page. */
  onPrevious?: () => void;
  /** Left out on the last page. */
  onNext?: () => void;
}

/**
 * design.md _Data display_, spec 0001 UX-14: Previous, Next, and the page size, below a list. Lazy like `Table` (PRM-10).
 * A button with nowhere to go stays focusable (PRM-4), so reaching the last page doesn't drop focus.
 */
export function Pagination({
  pageSize,
  pageSizes,
  onPageSizeChange,
  onPrevious,
  onNext,
}: PaginationProps) {
  const { t } = useLingui();
  return (
    <nav aria-label={t`Pagination`} className="flex flex-wrap items-center justify-end gap-2">
      <MenuTrigger trigger={<Button variant="ghost">{t`${pageSize} per page`}</Button>}>
        <Menu aria-label={t`Page size`}>
          <MenuSection
            title={t`Page size`}
            selectedKey={String(pageSize)}
            onSelect={(key) => {
              onPageSizeChange(Number(key));
            }}
          >
            {pageSizes.map((size) => (
              <MenuItem key={size} id={String(size)}>
                {String(size)}
              </MenuItem>
            ))}
          </MenuSection>
        </Menu>
      </MenuTrigger>
      <Button
        unavailableReason={onPrevious ? undefined : noPage}
        reasonMayChange
        onPress={onPrevious}
      >
        {t`Previous`}
      </Button>
      <Button unavailableReason={onNext ? undefined : noPage} reasonMayChange onPress={onNext}>
        {t`Next`}
      </Button>
    </nav>
  );
}
