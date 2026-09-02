import { playbackPosition, type PlaybackAnchor } from './timeline.ts'

type Media = Pick<HTMLMediaElement, 'readyState' | 'duration' | 'currentTime' | 'paused' | 'pause' | 'error'>
type MediaActions = { play: () => void; ended: () => void }

const DRIFT_SECONDS = 0.75

export function synchronizeMedia(media: Media, state: PlaybackAnchor, now: number, actions: MediaActions) {
  if (media.error || media.readyState < 1) return
  const duration = Number.isFinite(media.duration) ? media.duration : Infinity
  const target = playbackPosition(state, now, duration)
  snap(media, target)
  if (!state.isPlaying) { media.pause(); return }
  // A restored or seeking element need not emit ended; retry safely using the server revision.
  if (duration > 0 && target >= duration) { actions.ended(); return }
  if (media.paused) actions.play()
}

/**
 * Keeps a silent karaoke backdrop on the song's timeline. It carries no timeline of its own, so
 * a clip shorter than the song either restarts or holds its last frame.
 */
export function synchronizeBackground(
  media: Media, state: PlaybackAnchor, now: number, loop: boolean, play: () => void,
) {
  if (media.error || media.readyState < 1) return
  const duration = Number.isFinite(media.duration) ? media.duration : 0
  if (duration <= 0) return

  const position = playbackPosition(state, now)
  const finished = !loop && position >= duration
  snap(media, finished ? duration : loop ? position % duration : position)
  if (!state.isPlaying || finished) { media.pause(); return }
  if (media.paused) play()
}

function snap(media: Media, target: number) {
  if (Math.abs(media.currentTime - target) > DRIFT_SECONDS) media.currentTime = target
}
