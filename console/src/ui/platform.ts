// Spec 0015 PRM-11: Apple platforms use ⌘ (Meta) where others use Ctrl (Control). Read from
// `navigator.userAgentData?.platform`, falling back to `navigator.platform`.

interface NavigatorWithUaData extends Navigator {
  userAgentData?: { platform?: string };
}

export function isApplePlatform(): boolean {
  const nav = navigator as NavigatorWithUaData;
  const platform = nav.userAgentData?.platform || nav.platform || "";
  return /mac|iphone|ipad|ipod/i.test(platform);
}
