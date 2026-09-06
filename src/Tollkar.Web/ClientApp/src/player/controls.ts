export const controlsIdleDelayMs = 3000

export type ControlsActivity = 'pointer' | 'key'

/** A remote has no pointer to aim with, so its keys hand the focus to the panel unless it already holds it. */
export function takesControlsFocus(activity: ControlsActivity, panel: { visible: boolean, holdsFocus: boolean }): boolean {
  return activity === 'key' && !(panel.visible && panel.holdsFocus)
}

/** Focus deliberately holds nothing: on a television it always rests somewhere on the panel. */
export function holdsControls(hold: { hovered: boolean, seeking: boolean }): boolean {
  return hold.hovered || hold.seeking
}

export function idleRemainingMs(lastActivityMs: number, nowMs: number): number {
  return Math.max(0, Math.min(controlsIdleDelayMs, lastActivityMs + controlsIdleDelayMs - nowMs))
}
