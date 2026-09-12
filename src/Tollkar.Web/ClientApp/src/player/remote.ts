type PlaybackAction = 'play' | 'pause' | 'toggle'

export function remotePlaybackAction(event: { key: string; code: string; keyCode: number }): PlaybackAction | null {
  const names: Record<string, PlaybackAction> = { MediaPlay: 'play', MediaPause: 'pause', MediaPlayPause: 'toggle' }
  const codes: Record<number, PlaybackAction> = { 415: 'play', 19: 'pause', 10252: 'toggle', 179: 'toggle' }
  return names[event.key] ?? names[event.code] ?? codes[event.keyCode] ?? null
}
