import { useEffect, useState } from 'react'

export type Syllable = { text: string; startMs: number; endMs: number }
export type LyricLine = { startMs: number; endMs: number; syllables: Syllable[] }
export type KaraokeScript = { background: { loop: boolean } | null; lines: LyricLine[] }

/** The line being sung, or the one about to start before the first mark. */
export function activeLineIndex(lines: LyricLine[], positionMs: number) {
  let index = 0
  while (index + 1 < lines.length && lines[index + 1].startMs <= positionMs) index++
  return index
}

/**
 * How much of one syllable has been sung, 0 to 1. Marks give a start and an end only, so the
 * highlight is interpolated across that span.
 */
export function syllableFill(syllable: Syllable, positionMs: number) {
  if (positionMs >= syllable.endMs) return 1
  if (positionMs <= syllable.startMs) return 0
  const span = syllable.endMs - syllable.startMs
  return span > 0 ? (positionMs - syllable.startMs) / span : 1
}

export const lineText = (line: LyricLine) => line.syllables.map(syllable => syllable.text).join('')

/** Loads the script for a karaoke song; songs without one simply render as plain video. */
export function useKaraokeScript(songId: string | undefined) {
  const [loaded, setLoaded] = useState<{ songId: string; script: KaraokeScript } | null>(null)

  useEffect(() => {
    if (!songId) return

    const controller = new AbortController()
    void (async () => {
      try {
        const response = await fetch(`/api/songs/${encodeURIComponent(songId)}/karaoke`,
          { credentials: 'same-origin', signal: controller.signal })
        if (response.ok) setLoaded({ songId, script: await response.json() as KaraokeScript })
      } catch {
        // A karaoke song still plays without its text; the player stays usable.
      }
    })()

    return () => controller.abort()
  }, [songId])

  // Keyed by song so a script never lingers over the song that follows it.
  return loaded !== null && loaded.songId === songId ? loaded.script : null
}
