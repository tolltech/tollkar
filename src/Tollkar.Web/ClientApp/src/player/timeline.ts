export type PlaybackSnapshot = { revision: number; isPlaying: boolean; positionSeconds: number }
export type PlaybackAnchor = PlaybackSnapshot & { receivedAt: number }

export function playbackPosition(state: PlaybackAnchor, now: number, duration = Infinity) {
  const elapsed = state.isPlaying ? Math.max(0, now - state.receivedAt) / 1000 : 0
  return Math.min(Math.max(0, duration), state.positionSeconds + elapsed)
}

export function formatTime(seconds: number) {
  if (!Number.isFinite(seconds)) return '0:00'
  const value = Math.max(0, Math.floor(seconds))
  return `${Math.floor(value / 60)}:${String(value % 60).padStart(2, '0')}`
}
