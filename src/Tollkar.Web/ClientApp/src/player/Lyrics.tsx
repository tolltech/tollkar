import { useEffect, useRef, useState, type RefObject } from 'react'
import { activeLineIndex, lineText, syllableFill, type LyricLine } from './karaoke'

type LyricsProps = { lines: LyricLine[]; media: RefObject<HTMLVideoElement | null> }

/**
 * Draws the current and upcoming line over the backdrop. The highlight follows the media
 * element itself rather than React state: syllables turn over faster than the timeupdate event
 * fires, and re-rendering every frame would be wasteful. Each syllable is its own element, so
 * the fill lands on real glyph widths and a line that wraps still highlights correctly.
 */
export function Lyrics({ lines, media }: LyricsProps) {
  const [index, setIndex] = useState(0)
  const rendered = useRef(0)
  const line = useRef<HTMLParagraphElement>(null)
  // A song change swaps the lines before the next frame reports the new active one.
  const active = Math.min(index, Math.max(0, lines.length - 1))

  useEffect(() => { rendered.current = active })

  useEffect(() => {
    if (lines.length === 0) return
    let frame = 0

    function follow() {
      frame = requestAnimationFrame(follow)
      const positionMs = (media.current?.currentTime ?? 0) * 1000
      setIndex(activeLineIndex(lines, positionMs))
      // Paint the line the DOM actually holds; state reaches it only after the next commit.
      const syllables = lines[rendered.current]?.syllables ?? []
      const spans = line.current?.children
      for (let index = 0; spans && index < spans.length && index < syllables.length; index++) {
        (spans[index] as HTMLElement).style
          .setProperty('--sung', `${syllableFill(syllables[index], positionMs) * 100}%`)
      }
    }

    follow()
    return () => cancelAnimationFrame(frame)
  }, [lines, media])

  if (lines.length === 0) return null

  return <div className="player-lyrics" aria-live="off">
    <p className="player-lyric player-lyric-current" ref={line}>
      {lines[active].syllables.map((syllable, position) =>
        <span className="player-syllable" key={position}>{syllable.text}</span>)}
    </p>
    <p className="player-lyric player-lyric-next">{lines[active + 1] ? lineText(lines[active + 1]) : ''}</p>
  </div>
}
