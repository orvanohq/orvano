const nextFrame = () => new Promise((resolve) => requestAnimationFrame(resolve))

const running = () =>
  document
    .getAnimations()
    .filter(
      (animation) =>
        animation.playState === 'running' && animation.effect?.getTiming().iterations !== Infinity,
    )

/**
 * Waits until no finite CSS transition or animation is running on the page, so axe measures final colors.
 *
 * A fixed sleep races on a busy CI runner: a theme change starts `transition-colors` on inputs and buttons, and an
 * opening dialog fades in, so axe can read a color halfway between two themes. One wait is not enough either: a
 * transition can be cancelled and restarted toward a new value (a token that changes after another one), so this
 * checks again until a frame starts with nothing running. Endless animations (a spinner) are skipped, since they
 * never finish.
 */
export async function settleStyles(): Promise<void> {
  for (let round = 0; round < 20; round++) {
    // Two frames: the first applies pending styles, which is when the browser starts their transitions.
    await nextFrame()
    await nextFrame()
    const animations = running()
    if (animations.length === 0) return
    await Promise.all(animations.map((animation) => animation.finished.catch(() => undefined)))
  }
  throw new Error('Animations kept running after 20 rounds; is an endless one missing `infinite`?')
}
